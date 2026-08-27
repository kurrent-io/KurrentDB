# Kontext Quality Benchmarks

Measures how well Kontext ranks the right memories for a question, and how well its entity
pipeline decides what is the same thing — both scored against ground truth, so the numbers are
measured rather than claimed.

## Running it

```bash
cd src/Kontext/Kurrent.Kontext.Benchmarks
dotnet run -c Release                        # retrieval hill-climb over the chain's knobs
dotnet run -c Release -- --main-ab           # the shipped default against the legacy baseline
dotnet run -c Release -- --extraction-quality # what the extractor finds, per type
dotnet run -c Release -- --entities-quality  # resolution precision/recall (seconds, no NER model)
dotnet run -c Release -- --entities-ab       # the entity retrieval leg against the hybrid chain
dotnet run -c Release -- --entities-lab      # sweeps extraction and entity-leg knobs
dotnet run -c Release -- --determinism       # proves a chain returns the same ranking every run
```

Everything runs locally: embeddings on CPU via ONNX, an in-process store, and corpora committed to
the repository. No API keys, no external services. Results are deterministic — the same corpus and
pipelines produce the same numbers on every run. The `--entities-*` retrieval modes additionally
need the GLiNER model in the Embeddings Playground's `models/` cache; `--entities-quality` does
not, because its inputs are already-extracted surface forms.

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

LoCoMo `conv-26`, top-10 results per question (2026-08-21, `--main-ab`):

| pipeline | recall@1 | recall@5 | recall@10 | mrr | ndcg@10 | vs legacy |
|----------|---------:|---------:|----------:|----:|--------:|----------:|
| **default** | 0.2667 | 0.4600 | 0.5472 | 0.3773 | **0.4066** | +0.1009 |
| legacy | 0.1533 | 0.3700 | 0.4783 | 0.2597 | 0.3057 | — |

Relative to legacy: **ndcg@10 +33%**, recall@1 +74%, mrr +45%, recall@10 +14%.

Per-question head-to-head on ndcg@10: **default wins 56 questions, legacy wins 23, 71 tie**. The two
pipelines share on average only about a third of their top-10 results (Jaccard overlap 0.353), so
this is a genuinely different ranking, not a reshuffle of the same ten memories.

Rank fusion is tie-aware: candidates a leg scores identically share a competition rank, so
storage order never becomes a ranking signal — this is what makes the numbers above exactly
reproducible (the `--determinism` mode measures 150/150 identical outcomes across runs).

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

## The entity leg (`--entities-ab`)

`dotnet run -c Release -- --entities-ab` measures the shipped `Connected` chain — `Focused` plus
the entity retrieval leg — against `Focused`. It seeds the store's entity catalog the way
production ingestion does (GLiNER extraction over the production label vocabulary, the resolver's
full cascade, the production writer). Needs the GLiNER model in the Embeddings Playground's
`models/` cache (see that project's README).

LoCoMo `conv-26`, top-10 results per question (2026-08-21):

| pipeline | recall@1 | recall@5 | recall@10 | mrr | ndcg@10 | vs focused |
|----------|---------:|---------:|----------:|----:|--------:|-----------:|
| **connected** | 0.3106 | 0.4689 | 0.5522 | 0.4073 | **0.4273** | +0.0062 |
| focused | 0.3017 | 0.4822 | 0.5456 | 0.3979 | 0.4211 | — |
| connected, guards off | 0.2850 | 0.4739 | 0.5339 | 0.3854 | 0.4093 | -0.0118 |

Head-to-head on per-question ndcg@10: connected wins 6, focused wins 5, 139 tie. The leg's wins
are concentrated and large — a first hit moving #3→#1 or #4→#1 on questions naming a rare entity
("pottery class", "local church"), including one recovered miss — while its losses are small rank
slips inside topical clusters. The trade shows in the row: recall@1, mrr, recall@10 and ndcg@10
all up, recall@5 slightly down.

What made the leg pay for itself (it measured quality-neutral at best before):

- **extraction vocabulary** — five concrete labels (activity, animal, food, creative work, health
  condition) beside the POLE+O five roughly double the non-speaker catalog; abstract "event" and
  "object" stay silent on the everyday nominals these conversations hinge on
- **the resolver's lexical tier** — stem-identical forms ("pottery classes" = "pottery class"),
  near-identical single words ("Mell" = "Mel"), and unique prefixes for proper names ("Mel" =
  "Melanie", both directions) merge without touching the embedding model; Jaro-Winkler is gated
  to single words because shared phrase heads ("adoption interview" / "adoption meeting") are
  shared context, not shared identity
- **learned aliases** — a non-exact link writes the new surface form into the catalog, so the
  next mention exact-resolves and queries can name the entity by any learned form
- **morphology-folded matching** — aliases store a Porter-stemmed, determiner-stripped shape and
  the query folds to the same shape, so "camped" names "camping"
- **tie-aware rank fusion** — memories matching the same entity set tie exactly, and equal scores
  now share a competition rank in RRF; before, the leg's storage order was amplified into votes
  that displaced evidence
- **a tie-break, not a peer** — the measured constants (fusion weight 0.3, candidate cap 3,
  rare-entities-only scoring) keep the leg from handing the pool-local BM25 reread lexical
  distractors that displace evidence the vector leg found on meaning alone; the guards-off row
  shows what that protection is worth

The remaining headroom on this corpus is small by construction: 65 of the evidence memories share
no content words with their question (vector-leg territory no entity match can reach), and the
strongest losses left are rank slips between memories that genuinely mention the same entities.
A corpus with rare, richly-aliased entities remains the leg's best case; `--entities-lab` sweeps
extraction and leg knobs for that kind of investigation.

## Extraction (`--extraction-quality`)

`dotnet run -c Release -- --extraction-quality` scores what the extractor finds, against 45
labelled memories carrying 85 entities (`Corpus/Data/entity-extraction-labels.json`, every 9th
memory of conv-26). The shape follows the neo4j agent-memory extraction benchmark
(`benchmarks/metrics.py`): per-type precision/recall/F1, micro and macro averaged, beside latency
and throughput, with greedy one-to-one matching so a duplicate span cannot score twice.

Two deliberate deviations from that design. Their matcher demands an exact type match, but a
zero-shot vocabulary makes type a coin toss — "pottery" is defensibly a creative work, an activity
or an object — so a label here carries every defensible type, and the run reports an **untyped**
score beside the typed one. And matching compares the *folded* form rather than the raw string,
because that is the key the catalog and the entity leg actually use: "The sign" genuinely finds
"sign", and scoring it as a miss would report a failure the pipeline does not have.

| extractor | precision | recall | f1 | macro f1 | typed f1 | ms/doc |
|-----------|----------:|-------:|---:|---------:|---------:|-------:|
| POLE+O only, t=0.5 (legacy) | 92.2% | 55.3% | 69.1% | 43.5% | 69.1% | 13.3 |
| shipped, t=0.5, no split | 92.5% | 57.6% | 71.0% | 48.3% | 68.1% | 14.9 |
| **shipped, t=0.5** | **94.4%** | **60.0%** | **73.4%** | 51.2% | 70.5% | 13.7 |
| shipped, t=0.4 | 89.2% | 68.2% | 77.3% | 67.9% | 74.7% | 14.3 |
| shipped, t=0.35 | 87.5% | 74.1% | 80.3% | 76.4% | 76.4% | 14.0 |
| two-pass, t=0.5 | 92.7% | 60.0% | 72.9% | 53.0% | 71.4% | 25.8 |
| two-pass fp32, t=0.4 | 72.0% | 90.6% | 80.2% | 72.5% | 77.1% | 45.4 |

**Extraction recall is the binding constraint on the whole entity system.** Precision is high — an
extracted span is nearly always real — but two of every five entities a question could name are
never extracted, and nothing downstream can recover what was never found. That ceiling explains
the entity leg's modest retrieval contribution better than any ranking parameter does.

**Splitting coordinated spans is the one free win, and it ships.** Flat NER returns one span per
range, so "counseling and support groups" arrived as a single span and cost two entities;
`SpanSplitter` breaks it into the names it contains, reverting the split whenever any part cannot
stand alone as a name. Recall 57.6% → 60.0% *and* precision 92.5% → 94.4%, since the conjoined
spans were themselves false positives. Retrieval is unchanged.

**Macro F1 sits far below micro (51.2% vs 73.4%), and that gap is why both are reported.**
`person` is 40% of the labels and scores 95.8% F1, which in a micro average hides `activity` at
0.0%, `creative work` at 42.1% and `event` at 42.9% — exactly the discriminating types the
retrieval leg depends on.

**Lowering the threshold buys recall and loses retrieval, measured both ways.** t=0.35 is much
better extraction (recall 74.1%, macro F1 76.4%) and worse retrieval (ndcg +0.0062 → −0.0026,
swept in `--entities-lab` with every shipped guard active). The guards do not absorb it. The extra
spans are real entities by the labelling policy, but they are *generic* ones — "trip", "signs",
"event" — that add candidates without discriminating between memories.

That is the honest shape of the problem: **extraction recall as labelled here is not the same
objective as retrieval quality.** The labels count every namable thing equally; the retrieval leg
only benefits from rare, discriminating ones. Raising recall further has to come from finding more
*discriminating* entities, not from lowering the bar globally — which is why t=0.5 ships and the
better-scoring extractors do not.

## The resolver (`--entities-quality`)

`dotnet run -c Release -- --entities-quality` scores entity resolution **directly** instead of
through its echo in ranking. `Corpus/Data/entity-surface-forms.json` holds surface forms GLiNER
actually extracted from conv-26, grouped by hand into identity clusters; every form goes through
the production resolver and writer one at a time — the same incremental view ingestion has — and
the entity ids they land on are compared with the labels pairwise. Forms in one cluster must
merge, two clusters of the same type must not, and cross-type pairs are excluded because
type-strict resolution makes them trivially correct.

No NER model and no retrieval, so it runs in seconds and isolates *does the resolver put the same
thing together and different things apart* from *does the extractor find it at all*.

83 clusters, 115 surface forms, 789 same-type pairs (42 of them the same thing):

| resolver | precision | recall | f1 | wrong | missed | entities | bloat |
|----------|----------:|-------:|---:|------:|-------:|---------:|------:|
| legacy (exact + vector) | 100.0% | 14.3% | 25.0% | 0 | 36 | 109 | 131% |
| **shipped** | **100.0%** | **90.5%** | **95.0%** | 0 | 4 | 87 | 105% |

`legacy` is the cascade as it shipped before this work — an exact alias hit, then vector
similarity at 0.95, nothing lexical in between. It is kept runnable (`EntityResolverOptions.Legacy`)
for the same reason the retrieval chain keeps its `Legacy` composition: so the lexical tier is
priced rather than assumed.

**Recall went 14.3% → 90.5% with precision unmoved at 100%.** Legacy found 6 of the 42 merges the
corpus calls for; the shipped cascade finds 38, and still makes zero wrong ones. `bloat` is the
same result from the catalog's side: 109 entities for 83 real things becomes 87, so what used to
be a third of the catalog duplicated is now a twentieth.

Precision is the number to guard: a wrong merge corrupts the catalog permanently and silently,
while a missed merge only costs an alias. The four the shipped resolver misses are all phrase
containment — `Pottery` | `pottery project`, `pride parade` | `LGBTQ+ pride parade`,
`youth center` | `LGBTQ+ youth center`, `trans community` | `transgender community`.

**A containment tier was tried and rejected by measurement, then deleted.** Merging a phrase with
the shorter phrase it contains scored 63.1% / 97.6% / 76.6%: it fixes three of the four misses and
costs 24 wrong merges, because the merges cascade — `LGBTQ` claims `LGBTQ community`, which is then
the same entity as `LGBTQ youth`, `LGBTQ artists` and `youth center`. Each merge grows the blob the
next form joins, so the per-decision ambiguity guard never sees more than one candidate. Trading
100% precision for 7 points of recall is the wrong trade in a catalog that cannot un-merge.

The same run caught a defect in shipped code: the nickname-prefix tier was allowed on
organizations, so the bare label `LGBTQ` claimed `LGBTQ community` (2 wrong merges). Prefix
claiming is now person-only — nicknames are a person-name phenomenon. Removing those merges
changed the retrieval numbers by nothing, confirming they were noise.

Where the label set is weak: 42 positive pairs from one conversation, and the negative pairs it
can express are limited to what this corpus mentions. Precision 1.0 here means "no wrong merge
among the traps we could build", not "no wrong merge ever".

## Reading the numbers

- One dataset, 150 questions. Treat the deltas as point estimates on this sample rather than a
  guaranteed margin on every workload.
- The timing column the run prints is informational only, not a latency benchmark. Run-to-run
  variance exceeds the gap between the pipelines.
