# Day 1 — The first model call, from .NET

*The Architect's Hour — Mon 31 Aug 2026.*

**A model call is just an HTTP POST.** No SDK, no framework, no client library —
`HttpClient` and `System.Text.Json` are enough to talk to a language model. That's the
whole lesson, and this CLI is the proof.

## The part that matters

```csharp
var payload = new Dictionary<string, object?>
{
    ["model"] = Model,
    ["messages"] = new object[]
    {
        new { role = "system", content = system },
        new { role = "user",   content = user },
    },
};

using var req = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl)
{
    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
};
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

using var resp = await _http.SendAsync(req, ct);
```

The answer comes back at `choices[0].message.content`, the cost at `usage`. That's it.
Everything a vendor SDK does sits on top of these twenty lines.

## What's actually on the wire

Pass `--raw` and the app prints both bodies before it prints the answer, so you can
always see this for yourself rather than trusting a README:

```bash
dotnet run -- --provider=omniroute --prompt "Say hi in 5 words" --raw
```

The bodies go to stderr, so `2>/dev/null` gets you the clean answer back and
`1>/dev/null` gets you only the wire. The bearer token is never printed.

This is the whole request. One POST, three headers, a JSON body:

```http
POST /v1/chat/completions HTTP/1.1
Content-Type: application/json
Authorization: Bearer <token>

{
  "model": "auto/best-free",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user",   "content": "Say hi in 5 words" }
  ]
}
```

And the response, captured verbatim:

```json
{
  "id": "6c3367e0-c22d-4ab6-beb9-837ad0ac01af_5dc7b0f0...",
  "object": "chat.completion",
  "created": 1788192411,
  "model": "big-pickle",
  "choices": [
    {
      "index": 0,
      "finish_reason": "stop",
      "message": {
        "role": "assistant",
        "content": "Hi there, how are you?",
        "reasoning_content": "User wants greeting exactly 5 words. Provide exactly that. No extra."
      }
    }
  ],
  "usage": { "prompt_tokens": 128, "completion_tokens": 25, "total_tokens": 153 }
}
```

Three things I only noticed by looking at the bytes:

- **`model` in the response isn't the `model` I sent.** I asked for `auto/best-free`
  and got `big-pickle` — the gateway resolved an alias. What you request and what
  answers you are not the same thing.
- **`reasoning_content` sits next to `content`.** The model's private working, returned
  in the same payload. Some models put the *answer* there and leave `content` null,
  which is why the client falls back to it.
- **`usage` is where the money is**, and 128 prompt tokens for a six-word conversation
  means something upstream is adding to my prompt.

Because it's just a URL, a model name, and a bearer token, pointing at a *second*
provider was one small subclass — same POST, same parsing, different endpoint. Both
OmniRoute and NVIDIA speak the OpenAI `chat/completions` shape, so
[`OpenAiCompatibleClient`](Providers/OpenAiCompatibleClient.cs) does the work and each
provider file is ~30 lines.

![Two runs of the same CLI, one against NVIDIA NIM and one against OmniRoute, showing identical output formatting with different token counts and latency](assets/two-providers.png)

Same command, two backends, identical output shape — only the numbers move. Worth
staring at those numbers: `"hi"` cost 23 input tokens on one backend and 123 on the
other, and 370 output tokens bought a two-word reply. You're billed for tokens you
never see.

## Run it

```bash
dotnet run -- --provider=nvidia    --prompt "Explain an API gateway in 3 sentences."
dotnet run -- --provider=omniroute --prompt "Say hi"
echo "Say hi" | dotnet run -- --provider=omniroute
```

Optional: `--model`, `--temperature`, `--top-p`, `--raw`.

**OmniRoute** — a local gateway that routes through free-tier providers. Runs on
`localhost:20128`, no API key. Setup:
[my write-up](https://arunendapally.com/posts/omniroute-free-tier-routing-and-compression/).

**NVIDIA NIM** — grab a free key at [build.nvidia.com](https://build.nvidia.com/), then:

```bash
dotnet user-secrets set "NVIDIA_API_KEY" "your-key-here"   # or set the env var
```

Keys never touch a committed file — config resolves `env var → user-secret → default`.

## What actually broke

Four things went wrong on a build whose entire job was to send one HTTP request.

**1. The model I asked for was dead.** Two different NVIDIA defaults came back
`HTTP 410 end-of-life`. One had been retired the week before I went looking for it.
Model IDs are perishable in a way endpoint URLs never are — I'd been treating them like
a constant, and they're closer to a version number that expires.

**2. The model answered by saying nothing for 100 seconds.** No error, no partial
response, just a `TaskCanceledException` when `HttpClient`'s default timeout gave up. I
assumed a network problem and went looking in the wrong place. The actual answer was in
NVIDIA's own Python sample: reasoning models return *nothing* unless you send
`chat_template_kwargs: { enable_thinking: true }`. Silence isn't a hang, it's a
configuration you haven't set.

**3. "No active credentials for provider: openai" — on a gateway that was working.**
I'd pointed at `openai/gpt-4o` and got a credentials error from a machine that was
demonstrably talking to models fine. The fix was a named alias (`auto/best-free`), not a
provider/model pair. What you *point at* decides what you hit, and the response proves
it: I asked for `auto/best-free` and `big-pickle` answered.

**4. The one that cost me the most time — a variable I never set.** The same binary,
on the same machine, returned `HTTP 404` from one shell and a clean answer from
another. The cause: I'd named my config `ANTHROPIC_BASE_URL`, matching OmniRoute's own
setup instructions. But Claude Code and every Anthropic SDK read that same variable, so
on any machine with those installed, my app silently POSTed to `api.anthropic.com`
instead of the local gateway. The 404 was correct. My variable names were not.

Environment variables are a shared namespace and nobody owns a prefix. The config in
this repo is `OMNIROUTE_*` now.

## The stack, on one page

```
   model  →  prompt  →  retrieval  →  tools  →  agent  →  evaluation
     ▲
  you are here
```

| Box | What it is | Where it lands |
|---|---|---|
| **model** | one POST, one answer, priced in tokens | Day 1 — this repo |
| **prompt** | what you put in the messages array, and how you version it | ahead |
| **retrieval** | filling that array with facts the model doesn't have | ahead |
| **tools** | letting the model ask *you* to run something, then calling back | ahead |
| **agent** | a loop over model + tools that decides when it's done | ahead |
| **evaluation** | how you know any of it works after you change a prompt | ahead |

Every box above is the same HTTP call underneath. The rest is what you put in the
messages array, how many times you call it, and how you know it worked.

---

*Design notes in [`SPEC.md`](SPEC.md).*
