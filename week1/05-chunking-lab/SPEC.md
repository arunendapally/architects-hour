# SPEC — Chunking Lab: the right way to chunk, with evidence

*Planned 03 Sep 2026.*

## What it is

A follow-up to D2, answering the reader question "is the chunk size a fixed number, and
does that lose context?" with evidence instead of opinion. It adds a **meaning-aware
chunker** (paragraph-then-sentence) and an **eval sweep** that measures retrieval
recall@k across chunkers and sizes. The output is a table, not a guess.

## Why

D2 published with a 250-word default and a 2048-dim embedding; readers asked whether a
fixed number loses context. D3 deliberately broke chunking (naive char-count) to show the
failure but fixed nothing. This project turns "what's the right size" into a measurable
question.

## The chunkers compared

| Chunker | Strategy | Splits mid-sentence? |
|---|---|---|
| **word** (D2's, baseline) | sentence-boundary merge to a word target | no (sentence-aware already) |
| **semantic** (new) | paragraph first, sentence-split only an oversized paragraph | no |
| **char** (D3's, "wrong way") | naive fixed character count | **yes** |

The design deliberately reuses D2's sentence-aware word chunker and D3's naive char
chunker as baselines, so the only genuinely new code is the semantic chunker and the eval.

## Eval design

- **Ground truth, chunk-size-agnostic:** each question carries a golden *full sentence*
  (verbatim from the corpus). Correct retrieval = that sentence appears whole in a top-k
  chunk. Because the golden is a whole sentence, a chunker that splits mid-sentence can't
  ever retrieve it. Note what this does and doesn't measure: when the chunk budget is
  smaller than the golden sentence, the score is 0 by arithmetic, not by ranking. That is
  a real failure (the answer no longer exists whole in the index), but it is not a search
  result, and the README must say so rather than call it "recall collapsing".
- **Efficient:** questions embedded once; only chunks are re-embedded per (chunker, size).
- **Sweep:** 3 chunkers × sizes `[10, 20, 50, 100, 250, 500]`, top-k 3.
- **Metric:** `recall@k` = fraction of questions whose golden sentence appears in the top-k.
- **End-to-end:** `answer` mode retrieves top-k + calls the chat model for a grounded
  answer, as a spot check on top of the recall table.

## Files

| File | What it does |
|---|---|
| `Chunkers.cs` | `SemanticChunker` — paragraph pass, delegating oversized paragraphs to D2's chunker |
| `Eval.cs` | recall@k sweep (load questions, embed once, score each chunker/size) |
| `Program.cs` | CLI: `eval` / `chunk` / `answer` modes |
| `data/corpus.txt` | A ~27-paragraph handbook (single-topic facts, localized answers) |
| `data/eval-questions.tsv` | 13 questions, each with a golden full sentence from the corpus |

Corpus: `data/corpus.txt` (an employee handbook, so answers are local and non-overlapping —
good for discriminating retrieval).

## Config / CLI

Same resolution as D1-D4; `NVIDIA_API_KEY` via shared secret store.

```
--mode eval|chunk|answer     (default eval)
--doc <path>                 default data/corpus.txt
--questions <path>           default data/eval-questions.tsv
--sizes 10,20,50,...         default 10,20,50,100,250,500
--chunker word|semantic|char for chunk/answer modes (default semantic)
--size N                     chunk size in words for chunk/answer (default 250)
--overlap N                  overlap words for chunk/answer (default 25)
--top-k 3
--query "..."                for answer mode (or stdin)
```

## Non-goals

- No vector DB, no rerank, no framework.
- No LLM-based semantic chunking (heavier, out of scope).
- No change to D2/D3's published behavior; they're referenced as-is.
