# The Architect's Hour

Learning how to build AI *into applications* — model, prompt, retrieval, tools,
agent, evaluation — one box at a time, by hand, in .NET. One hour a day.

This is about putting AI in the software you ship. It isn't about using AI
assistants to write code. Every day is a working app you can clone and run.

Each day is a self-contained project with a `README.md` (what's in the code, how
to set it up, how to run it) and a `SPEC.md` (what was planned, before building).

## Week 1 — By hand, before any framework

| Day | Lesson | Stack |
|---|---|---|
| 01 | [The first model call, from .NET](week1/01-first-call-to-model-dotnet/) — a provider-agnostic CLI, raw `HttpClient`, two model backends behind one interface | .NET 9 |
| 02 | [RAG by hand, no framework](week1/02-rag-by-hand/) — chunk a document, get embeddings, cosine similarity in own code, stuff top chunks into prompt | .NET 9 |

## Running anything here

Each project is a standalone `dotnet run`. Secrets are never committed — the
apps read `env var → user-secret → default`:

```bash
dotnet user-secrets set "NVIDIA_API_KEY" "your-key-here"
```
