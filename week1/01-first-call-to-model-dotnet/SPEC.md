# D1 — Provider-agnostic model CLI — Spec

## Goal
A .NET console app that sends a prompt to a model and prints the response +
usage metrics, with the provider swappable via configuration.

Makes the first model call from .NET, and (this week's ethos) does it by hand —
raw HTTP, no framework, no vendor SDK. The wire format stays visible.

## Interface
One invocation = one call. Provider from flag or env var; prompt from flag or stdin.

```
dotnet run -- --provider=omniroute --prompt "Explain an API gateway in 3 sentences"
dotnet run -- --provider=nvidia --prompt "..." --temperature 1 --top-p 0.95
echo "..." | dotnet run -- --provider=omniroute
```

| Flag | Env fallback | Default |
|---|---|---|
| `--provider` | — | `omniroute` |
| `--prompt` | — (else stdin) | — |
| `--model` | provider-specific (`ANTHROPIC_MODEL`, `NVIDIA_NIM_MODEL`) | provider default |
| `--temperature` | — | model default |
| `--top-p` | — | model default |

## Provider layer
One thin abstraction. All providers return the same shape.

```csharp
public interface IModelClient
{
    Task<ModelResponse> CompleteAsync(string system, string user, CancellationToken ct = default);
}

public record ModelResponse(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);
```

`Latency` measured in one place (the caller) so it's comparable across
providers regardless of transport details.

## Providers (built — scope for D1)

| Provider | Endpoint | Config |
|---|---|---|
| `omniroute` | `POST {ANTHROPIC_BASE_URL}/chat/completions` | `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN` |
| `nvidia` | `POST https://integrate.api.nvidia.com/v1/chat/completions` | `NVIDIA_API_KEY`, optional `NVIDIA_NIM_MODEL` |

Both speak the OpenAI chat.completions shape, so they share the
`OpenAiCompatibleClient` base, which owns POST + response parsing + usage
normalization (`usage.prompt_tokens`/`completion_tokens` **and**
`reasoning_content` fallback). Each subclass only supplies endpoint, model, and
bearer token — this is the "generic interface" payoff. A provider with a
different wire shape would add a sibling client implementing `IModelClient`
directly. Only two provide sane defaults; add others as needed.

## Transport
Raw `HttpClient` + `System.Text.Json`. No vendor SDKs — consistent with the
week ("build by hand before touching a framework"), and it keeps exactly what
hits the wire visible, which is the D1 note to capture.

## Output
```
--- omniroute : Explain an API gateway in 3 sentences ---
──────────────────────────────────────────────────────────
<response content>
──────────────────────────────────────────────────────────
Input: 135 tokens   Output: 131 tokens   Total: 266   Latency: 10344 ms
```

## Architecture
```
Program.cs                          — arg/env parse, provider selection, print result
ModelClient.cs                      — IModelClient interface + ModelResponse
Providers/
  OpenAiCompatibleClient.cs         // shared POST + parse for OpenAI-shaped providers
  OmniRouteClient.cs  : OpenAiCompatibleClient    // local aggregator proxy
  NvidiaNimClient.cs  : OpenAiCompatibleClient    // NVIDIA NIM (reasoning kwargs)
```
The interface (`IModelClient`) is the contract everything derives from;
`OpenAiCompatibleClient` is the concrete shared base for the two built providers.

## Non-goals (YAGNI)
- No `--compare` multi-provider run, no streaming, no tool calls, no retries/backoff.
  Deferred items tracked in `myDocs-gitignore/improvements.md`.
- No secrets in code — `dotnet user-secrets` / env vars, never committed.

## Verify
1. `dotnet build` clean.
2. With a live key for at least one provider, `dotnet run -- --provider=X --prompt "Say hi"` prints a non-error response and token/latency numbers.
3. Response shape identical across providers (only content + usage differ).
