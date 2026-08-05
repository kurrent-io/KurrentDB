# Kontext Retrieval Quality Benchmark

Measures how well Kontext ranks the right memories for a question, scored against ground truth. It
compares the current default retrieval pipeline with the previous (legacy) one on a public
long-term-conversation dataset, so the improvement is measured, not claimed.

## Running it

```bash
cd src/Kontext/Kurrent.Kontext.Benchmarks
dotnet run -c Release
```

Everything runs locally: embeddings on CPU via ONNX, an in-process store, and a corpus committed to
the repository. No API keys, no external services. Results are deterministic — the same corpus and
pipelines produce the same numbers on every run.

## Methodology

The benchmark runs the exact retrieval pipelines that ship in Kontext, not benchmark-only copies.
Every question in the corpus is asked of both pipelines with a limit of 10 results, and the returned
memories are scored against the conversation turns the dataset cites as evidence for the answer.

The dataset is LoCoMo conversation 26: **419 memories and 150 questions** drawn from a published
benchmark for long-term conversational memory.

| Metric | What it rewards |
|--------|-----------------|
| `recall@k` | how much of the evidence appears anywhere in the top k results |
| `mrr` | how early the *first* relevant memory appears (1/rank) |
| `ndcg@10` | finding the evidence **and** ranking it high — the headline number |

Relevance is binary, every question carries equal weight, and questions with no ground truth are
skipped rather than scored zero.

## Results

LoCoMo `conv-26`, top-10 results per question:

| pipeline | recall@1 | recall@5 | recall@10 | mrr | ndcg@10 | vs legacy |
|----------|---------:|---------:|----------:|----:|--------:|----------:|
| **default** | 0.2767 | 0.4700 | 0.5589 | 0.3832 | **0.4120** | +0.1040 |
| legacy | 0.1700 | 0.3767 | 0.4617 | 0.2662 | 0.3079 | — |

Relative to legacy: **ndcg@10 +34%**, recall@1 +63%, mrr +44%, recall@10 +21%.

Per-question head-to-head on ndcg@10: **default wins 54 questions, legacy wins 20, 76 tie**. The two
pipelines share on average only about a third of their top-10 results (Jaccard overlap 0.364), so
this is a genuinely different ranking, not a reshuffle of the same ten memories.

The gain concentrates where it matters most for an agent consuming the results: the default's
largest wins move the first relevant memory from around position 7–8 up to position 1.

## What improved

Both pipelines run a semantic (vector) search and a keyword search, fuse the two result lists with
reciprocal-rank fusion, and diversify the final list with MMR. The default pipeline adds two ranking
stages between fusion and diversification:

- **BM25 re-ranking** over the fused candidates, sharpening lexical relevance to the question
- **cognitive modulation**, weighting each memory's recency, importance, relevance, and certainty

At this benchmark's settings both pipelines rank an identical 30-candidate pool, so the measured
gain comes from ranking the candidates better, not from fetching more of them.

## Reading the numbers

- One dataset, 150 questions. Treat the deltas as point estimates on this sample rather than a
  guaranteed margin on every workload.
- The timing column the run prints is informational only, not a latency benchmark. Run-to-run
  variance exceeds the gap between the pipelines.
