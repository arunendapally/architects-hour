# D1 — The first model call, from .NET

*The Architect's Hour, Day 1 — Mon 31 Aug 2026.*

A console app that sends a prompt to a model and prints the answer. Then I made it
talk to *two different model backends* — the Oracle that gives you those magic words.

## The thing I actually built

A tiny, dependency-free CLI that opens a socket to a language model and says hello:

```
dotnet run -- --provider=nvidia --prompt "Explain an API gateway in 3 sentences."

--- nvidia : Explain an API gateway in 3 sentences ---
An API gateway is a server that acts as the single entry point for client requests,
routing them to the appropriate backend services...
──────────────────────────────────────────────────────────
Input: 34 tokens   Output: 336 tokens   Total: 370   Latency: 33967 ms
```

No framework. No SDK. Just `HttpClient` + `System.Text.Json` — because this whole week
is about doing the thing by hand once, so no framework can ever confuse me about what
it's actually doing underneath.

## The "generic interface" — the part that paid for itself

Two conversations later I had a second provider to add (NVIDIA NIM). The scariest
moment in any "swap the backend" story is the second one — that's when you find out
your abstraction was a lie.

Here it wasn't. Both providers speak the same OpenAI-shape wire protocol, so:

```csharp
public interface IModelClient
{
    Task<ModelResponse> CompleteAsync(string system, string user, CancellationToken ct = default);
}

public record ModelResponse(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);
```

Adding NVIDIA was **one new file** — supply the endpoint, the model name, the bearer
token — and everything else (POST, parse, usage numbers) came from the shared base:

```csharp
// OmniRouteClient.cs  — the local aggregator proxy
protected override Uri ChatCompletionsUrl => new($"{_endpoint}/chat/completions");
protected override string Model => _model;
protected override string BearerToken => _token;

// NvidiaNimClient.cs   — NVIDIA NIM
protected override Uri ChatCompletionsUrl => _url;
protected override string Model => _model;
protected override string BearerToken => _token;
```

If I'd hardcoded "openai/gpt-4o", the second provider would've been a copy-paste fork.
Because the transport was behind one interface, it was a subclass. That's the
generic-interface payoff, and it showed up on day 1.

## What actually bit me (the honest part)

**1. "No active credentials" — the alias, not the provider.**
I pointed at `openai/gpt-4o` on my local aggregator and got `No active credentials for
provider: openai`. But the machine was already talking to the model fine. The fix was a
named **alias** (`auto/best-free`), not a provider/model combo. The client config, not
the bytes, decides what you're hitting.

**2. Models are perishable.**
NVIDIA retires hosted models fast. My first two defaults returned `HTTP 410 end-of-life`
— the model was retired a week before I looked at it. The third timed out. Lesson:
never assume a model ID stays alive; check the catalog.

**3. A reasoning model that won't talk until you ask.**
The NVIDIA model sat silent for 100 s and timed out. NVIDIA's own sample was the clue:
reasoning models return *nothing* unless you send
`chat_template_kwargs: { enable_thinking: true }`. One grabbed sentence of docs was
worth more than an hour of guessing.

## Secrets in a public repo

Keys never touch a committed file. `.gitignore` + `dotnet user-secrets`: the app reads
`env var → user-secret → default`. Public repo stays clean, prod just sets an env var.

```bash
# first time — set your NVIDIA key (stored in %APPDATA%\Microsoft\UserSecrets)
dotnet user-secrets set "NVIDIA_API_KEY" "your-key-here"

# or, in prod / CI — set an env var instead (takes precedence over user-secrets)
export NVIDIA_API_KEY="your-key-here"
```

OmniRoute needs no key — it's a local proxy with no auth gate.

## The stack, on one page

```
model → prompt → retrieval → tools → agent → evaluation
   ↑ (this is today: the very first hop)
```

Today was the *model* box. The rest of the week fills in whichever I need — but nothing
builds on a foundation I can't touch by hand.

---

*Code lives in [`week1/01-first-call-to-model-dotnet/`](.). Build notes and deferred
ideas are in my private notes, not this repo.*