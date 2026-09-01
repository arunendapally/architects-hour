# Day 1 — Calling a model from .NET

*Part of [**The Architect's Hour**](../../) — learning to build AI into applications,
one hour a day. Mon 31 Aug 2026.*

A console app that sends a prompt to a language model and prints the answer.

**A model call is just an HTTP POST.** No SDK, no framework, no client library.
`HttpClient` and `System.Text.Json` are the whole dependency list.

## What's in the code

Five files, 330 lines including comments.

| File | What it does |
|---|---|
| `Program.cs` | Reads the command-line args, picks a provider, prints the result |
| `ModelClient.cs` | The `IModelClient` interface, and the `ModelResponse` / `RequestOptions` records |
| `Providers/OpenAiCompatibleClient.cs` | **The important one.** Builds the JSON, POSTs it, parses the reply |
| `Providers/OmniRouteClient.cs` | Endpoint + token + model for OmniRoute |
| `Providers/NvidiaNimClient.cs` | Endpoint + token + model for NVIDIA |

The two provider files are 38 and 51 lines, and contain no HTTP code at all. They just
supply a URL, a model name, and a bearer token — everything else is shared. That's
possible because both services accept the same request format (OpenAI's
`chat/completions`), which has become the de-facto standard.

## The call itself

This is the entire thing, from `OpenAiCompatibleClient.cs`:

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

The answer comes back at `choices[0].message.content`. What it cost you is at `usage`.

Two things worth noticing:

- **`messages` is the whole conversation.** The server remembers nothing between calls.
  If you want a chatbot, you send the entire history every time.
- **`system` vs `user` roles.** The system message sets behaviour ("You are a helpful
  assistant"), the user message is the question.

## Setup

You need one provider. Either works.

### Option A — OmniRoute (no API key)

A gateway that runs on your own machine and routes to free-tier models. Nothing to sign
up for. Install and start it by following
[my write-up](https://arunendapally.com/posts/omniroute-free-tier-routing-and-compression/),
then confirm it's listening on `http://localhost:20128`.

No configuration needed — that address is the default.

### Option B — NVIDIA NIM (free API key)

1. Go to [build.nvidia.com](https://build.nvidia.com/) and sign in.
2. Pick any model, click **Get API Key**, copy it. It starts with `nvapi-`.
3. Store it. Two ways:

```bash
# Preferred — user-secrets. Stored outside the repo, so it can't be committed by accident.
dotnet user-secrets set "NVIDIA_API_KEY" "nvapi-your-key-here"
```

```bash
# Or an environment variable (bash / git bash)
export NVIDIA_API_KEY="nvapi-your-key-here"
```

```powershell
# Or an environment variable (PowerShell)
$env:NVIDIA_API_KEY = "nvapi-your-key-here"
```

**Never put the key in a file inside the repo.** User-secrets live in
`%APPDATA%\Microsoft\UserSecrets\` on Windows, which is why they're safe.

### All the settings

Every one is optional except the NVIDIA key. Each resolves in this order:
**environment variable → user-secret → built-in default.**

| Setting | Used by | Default |
|---|---|---|
| `NVIDIA_API_KEY` | nvidia | *(none — required)* |
| `NVIDIA_NIM_MODEL` | nvidia | `nvidia/nemotron-3.5-lightning-30b-a3b` |
| `OMNIROUTE_BASE_URL` | omniroute | `http://localhost:20128/v1` |
| `OMNIROUTE_AUTH_TOKEN` | omniroute | `omniroute` |
| `OMNIROUTE_MODEL` | omniroute | `auto/best-free` |

They're named `OMNIROUTE_*` on purpose. OmniRoute's own setup guide uses
`ANTHROPIC_BASE_URL`, but Claude Code and the Anthropic SDKs read that same name — so on
a machine with those installed, this app would silently POST to the wrong server. Env
vars are a shared namespace and nobody owns a prefix.

## Run it

```bash
cd week1/01-first-call-to-model-dotnet
dotnet run -- --provider=omniroute --prompt "Explain an API gateway in 3 sentences."
```

```bash
dotnet run -- --provider=nvidia --prompt "Explain an API gateway in 3 sentences."
```

You can also pipe the prompt in:

```bash
echo "Explain an API gateway in 3 sentences." | dotnet run -- --provider=nvidia
```

![Two runs of the same CLI, one against NVIDIA NIM and one against OmniRoute, showing identical output formatting with different token counts and latency](assets/two-providers.png)

Same command, two backends, same output shape. Only the numbers change.

## Seeing what's actually sent

Add `--raw` to print the request and response bodies:

```bash
dotnet run -- --provider=omniroute --prompt "Say hi in 5 words" --raw
```

```json
{
  "model": "auto/best-free",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user",   "content": "Say hi in 5 words" }
  ]
}
```

```json
{
  "model": "big-pickle",
  "choices": [ { "message": {
      "role": "assistant",
      "content": "Hi there, how are you?",
      "reasoning_content": "User wants greeting exactly 5 words. Provide exactly that."
  } } ],
  "usage": { "prompt_tokens": 128, "completion_tokens": 25, "total_tokens": 153 }
}
```

Three things this shows that you'd otherwise miss:

- **I asked for `auto/best-free` and `big-pickle` answered.** The gateway resolved an
  alias. What you request isn't always what serves you.
- **`reasoning_content`** is the model's private working. Some models leave `content`
  empty and put the answer there instead.
- **128 prompt tokens for a six-word conversation** — something upstream is adding to
  your prompt, and you're paying for it.

`--raw` writes to stderr, so `2>/dev/null` still gives you a clean answer to pipe
elsewhere. The API key is never printed.

## The knobs: temperature, top_p, top_k

A model doesn't pick the next word. It produces a probability for *every* word in its
vocabulary, and then something chooses from that list. These three settings control the
choosing.

**`temperature`** (0 to ~2) flattens or sharpens the probabilities.
Low (0–0.3) makes the highest-probability word win almost every time — repeatable,
predictable, best for extraction and classification. High (0.8–1.2) evens things out so
unlikely words get a real chance — better for brainstorming and creative writing. At 0
you get near-identical output for the same prompt every time.

**`top_p`** (0 to 1), also called nucleus sampling, cuts the list short. At `0.9` the
model sorts words by probability, keeps adding them until they total 90% of the
probability mass, and ignores everything else. It adapts: when the model is confident
the list is tiny, when it's unsure the list is long.

**`top_k`** keeps a fixed number of candidates instead — `top_k=40` means "only ever
consider the 40 most likely words," regardless of confidence.

Rule of thumb: **change one, not both.** Temperature and `top_p` interact in ways that
are hard to reason about. Most people leave `top_p` at its default and adjust
temperature only.

```bash
dotnet run -- --provider=nvidia --prompt "Name 5 uses for a brick" --temperature 1.2
dotnet run -- --provider=nvidia --prompt "Extract the date: invoice due 14 March" --temperature 0
```

This CLI exposes `--temperature` and `--top-p` but **not** `--top-k`, because `top_k`
isn't part of OpenAI's `chat/completions` spec — OpenAI ignores it, NVIDIA accepts it as
an extension. Provider-specific fields go through the `ExtraBody()` override, which is
how `NvidiaNimClient` already sends its reasoning settings.

Note that reasoning models often override these anyway. Run with `--raw` and compare
what you sent against what came back.

## What broke

- **The model I asked for was dead.** Two NVIDIA defaults returned `HTTP 410
  end-of-life`. Model IDs expire; endpoint URLs don't.
- **A reasoning model returned nothing for 100 seconds**, then timed out. Not a network
  problem — NVIDIA's reasoning models stay silent unless you send
  `chat_template_kwargs: { enable_thinking: true }`.
- **The same binary gave a 404 in one shell and worked in another.** That's the
  `ANTHROPIC_BASE_URL` collision described above. It cost the most time of anything here.

## The stack

Today was the first box.

```
   model  →  prompt  →  retrieval  →  tools  →  agent  →  evaluation
     ▲
  you are here
```

| Box | What it is |
|---|---|
| **model** | one POST, one answer, priced in tokens |
| **prompt** | what you put in the messages array, and how you version it |
| **retrieval** | filling that array with facts the model doesn't have |
| **tools** | letting the model ask *you* to run something, then calling back |
| **agent** | a loop over model and tools that decides when it's done |
| **evaluation** | how you know it still works after you change a prompt |

Every one of those is the same HTTP call underneath. The difference is what goes in the
messages array, how many times you call it, and how you check the result.

---

*Design notes in [`SPEC.md`](SPEC.md).*
