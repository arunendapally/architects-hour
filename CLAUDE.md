# CLAUDE.md — The Architect's Hour

**Purpose: learning how to build AI into applications** — calling models, RAG,
tools, agents, evaluation — for me and for anyone else reading. This is *not*
about using AI assistants to write code. Every day is a working app, not a
workflow tip.

Daily builds, one per weekday, each a self-contained project. Public repo.
Written so a stranger can clone it and learn from it, not as a personal log.

## Layout

`weekN/NN-short-slug/` — numeric prefix, descriptive name. Each has `README.md`
(the reader-facing page) and `SPEC.md` (what was planned, before building).

Private notes live in `myDocs-gitignore/` — gitignored. Never link to it from a
committed file; those links 404 for everyone else.

## README = practical. Blog post = opinion.

The README is for someone who wants to run the thing. Fixed shape:

1. What's in the code — file table, what each does
2. Setup — how to get the API key, where to put it, every setting and its default
3. Run it — commands they can paste
4. The concepts this day introduces — short, tied to this code, not a docs rewrite
5. What broke — 3 bullets, no narrative
6. The stack diagram — which box today was

KISS. Tables and commands over prose. Ten minutes to write, not an hour.

The failure narrative, the story, and the argument go in the Friday blog post —
not the README. Lead the post with the failure.

## Code

- Hand-rolled before frameworks. No vendor SDKs unless the day is about the SDK.
- YAGNI: no flags, abstractions, or error handling nobody asked for.
- Errors → message on stderr + non-zero exit. Never a stack trace.
- Keep `SPEC.md` in step with the code; drift is a bug.

## Secrets and config

- Never commit a key. `dotnet user-secrets`, or env var. Resolution order is
  env var → user-secret → default.
- Prefix env vars with the thing they configure (`OMNIROUTE_*`), never with a
  vendor name someone else's tooling reads (`ANTHROPIC_*`, `OPENAI_*`). Learned
  the hard way — see Day 1.
- Redact tokens in any debug output.

## Before publishing

- Verify every number in the prose — line counts, token counts, timings. Don't
  round from memory.
- Never invent a detail to make a story read better. If it didn't happen, cut it.
- Build clean and run it once against a live provider.
