# SPEC — D2: RAG by hand, no framework

*Planned 01 Sep 2026.*

## What it is

Day 2 adds **retrieval** to the stack. The entire RAG loop — chunk a document, get
embeddings over raw HTTP, find the most similar chunks with cosine similarity in our own
code, stuff the top chunks into the prompt, and call a chat model — with no framework,
no vector DB, no SDK. The chat hop reuses D1's `IModelClient`.

## Why manual

The point is to build the retrieval half once by hand so no framework can ever confuse
you about what it's doing. Every step — chunking, embedding, similarity, prompt
assembly — is a few dozen lines of explicit code.

## Provider choices

- **Embeddings**: NVIDIA NIM only (`nvidia/nemotron-3-embed-1b`, 2048 dims, free tier).
  OpenAI-compatible endpoint at `https://integrate.api.nvidia.com/v1/embeddings`.
  Requires `input_type` parameter: `"passage"` for document chunks, `"query"` for
  user questions.
- **Chat**: Reuses D1's `NvidiaNimClient` via `ProjectReference` to `D1.csproj`. No new
  chat code at all. OmniRoute is deliberately not wired up here: it can't serve the
  embedding hop, and carrying a second provider for half the pipeline costs a second
  secret and buys nothing on a retrieval day.

NVIDIA NIM embedding model discovery was the main debugging task: the model name
(`nvidia/embed-qa-4` → `nvidia/nemotron-3-embed-1b`) and the required `input_type`
parameter are not documented clearly and were found by browsing build.nvidia.com.

## Pipeline

```
read doc → sentence-aware chunk → embed all chunks (one batch) →
embed query (input_type: "query") → cosine top-k → build context prompt →
D1.IModelClient.CompleteAsync → print answer + chunk indices + latencies
```

## Files

| File | What it does |
|---|---|
| `Program.cs` | CLI flags + full pipeline orchestration |
| `Chunking.cs` | Sentence-aware chunking with overlap |
| `Similarity.cs` | L2 normalize + dot product + top-k |
| `Embedding.cs` | `IEmbedder` interface + `EmbedderBase` (mirrors D1's `IModelClient` pattern) |
| `Providers/OpenAiEmbeddingClient.cs` | Shared base for OpenAI-shaped `/embeddings` POST |
| `Providers/NvidiNimEmbeddingClient.cs` | NVIDIA NIM endpoint + model + token |
| `data/sample.txt` | Bundled corpus (18 paragraphs, 1,140 words → 6 chunks at defaults) |

## Config

Same resolution as D1: env var → user-secret → default.

| Variable | Default | Notes |
|---|---|---|
| `NVIDIA_API_KEY` | *(none, required)* | Same secret as D1; the only one D2 needs |
| `NVIDIA_EMBEDDING_MODEL` | `nvidia/nemotron-3-embed-1b` | Embeddings, 2048 dims |
| `NVIDIA_NIM_MODEL` | `nvidia/nemotron-3.5-lightning-30b-a3b` | Chat, D1's default |

## CLI flags

```
--doc <path>            default data/sample.txt
--query "..."           or pipe on stdin
--top-k 3               number of chunks to retrieve
--chunk-size 250        target words per chunk
--chunk-overlap 25      words shared between adjacent chunks
```

## What it doesn't do

- No vector database. Vectors live in a `List<float[]>` in memory.
- No reranking. Cosine similarity is the only scoring step.
- No streaming. Both embedding and chat calls are await-then-print.
- No evaluation metrics. Correctness is verified by running queries against the
  sample doc and checking that retrieved chunks are relevant.

## Line count

364 lines across 6 source files. Core retrieval logic (chunking + similarity +
embedding interface) is 166 lines. The rest is provider HTTP plumbing (102 lines)
and orchestration — deliberately visible so the student sees what a framework hides.
