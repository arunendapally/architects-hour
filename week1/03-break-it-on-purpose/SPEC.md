# SPEC — D3: Break it on purpose

*Planned 02 Sep 2026.*

## What it is

Day 3 takes the D2 RAG pipeline and breaks it on purpose. The whole pipeline — embed,
cosine search, chat — is inherited from D2 unchanged. The only new code is a
**deliberately broken char-based chunker** that slices at a fixed character count and
ignores sentence boundaries, reproducing the doc's mistake #1 ("chunking that splits
sentences in half"). Run it tiny (50 chars) and huge (5000 chars), ask answerable and
unanswerable questions, and record the wrong answers verbatim for the post.

## Why break it

D2 built the pipeline correctly; D3 finds where it falls apart. Tiny chunks shatter
sentences and make retrieval miss answers that are in the document; huge chunks collapse
retrieval into "everything or nothing." The wrong answers — captured verbatim — are the
week's post material. The point of building by hand is knowing *why* a framework's
chunk-size default matters; this is that knowledge made visible.

## What's new vs D2

Only the chunker. Everything else is a `ProjectReference` to D2, so the lesson is
isolated: the same embed + search + prompt that worked yesterday fails today purely
because of how the text was sliced.

| File | What it does |
|---|---|
| `CharChunker.cs` | The mistake: slices at exactly N characters, mid-sentence, with overlap |
| `Program.cs` | Reuses D2's embed/retrieve/chat; `--chars` drives the chunk size |

## Provider

Same as D2: NVIDIA NIM embeddings (`nvidia/nemotron-3-embed-1b`, 2048 dims) and NVIDIA
chat. One key (`NVIDIA_API_KEY`), inherited from D1/D2's shared secret store.

## CLI

```
--doc <path>      default ../02-rag-by-hand/data/sample.txt
--query "..."     or pipe on stdin
--chars 50        char-chunk size (the breakage lever)
--overlap 10      chars shared between adjacent chunks
--top-k 3         chunks to retrieve
```

## What it doesn't do

- No new embedding, retrieval, or chat code — all inherited from D2.
- No guardrail removal. The prompt keeps "say so plainly" so the break shows up in
  *retrieval*, not as a prompt bug. (The guardrail-stripped hallucination was a one-off
  experiment logged in the week notes, not a committed path.)
- No fix. The broken chunker stays broken on purpose.

## Expected results

- `--chars 50` → ~160 chunks of sentence shards; retrieval surfaces 3 fragments and the
  model reports the answer "is not contained," though it is.
- `--chars 5000` → ~2 chunks (whole doc); retrieval is vacuous and the answer comes back
  intact.
- Unanswerable questions → clean refusal while the guardrail holds.
