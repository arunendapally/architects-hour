# The Architect's Hour

One hour a day, building the AI-application stack by hand — model, prompt,
retrieval, tools, agent, evaluation — one box at a time.

Each day is a self-contained project with its own `README.md` (what I built and
what bit me) and `SPEC.md` (what I set out to build, before I built it).

## Week 1 — By hand, before any framework

| Day | Lesson | Stack |
|---|---|---|
| 01 | [The first model call, from .NET](week1/01-first-call-to-model-dotnet/) — a provider-agnostic CLI, raw `HttpClient`, two model backends behind one interface | .NET 9 |

## Running anything here

Each project is a standalone `dotnet run`. Secrets are never committed — the
apps read `env var → user-secret → default`:

```bash
dotnet user-secrets set "NVIDIA_API_KEY" "your-key-here"
```
