# How Kontext Retrieval Works — a junior-friendly guide

> A plain-English walkthrough of how KurrentDB **Kontext** answers a search/recall query. It describes
> the current code in `src/KurrentDB.Kontext/Search/` — `KontextService`, `Retriever`,
> `SearchAlgorithms.*`, and the cross-encoder reranker. **No machine-learning or search background is
> assumed** — every term is defined before it's used. This is a living doc; keep it in sync with the code.

## The one-sentence version

Given a query, Kontext returns the handful of stored events/memories most relevant to it — by combining
**keyword** search and **meaning-based** search, re-checking the best candidates with a smarter model, and
finally trimming out near-duplicates.

It is **not** "just a vector database." It's a small pipeline with five stages. Below we first learn the
building blocks, then walk the stages.

---

## Part 1 — the ideas you need first (plain English)

### Text → numbers: "embeddings" and "vectors"
A computer can't compare *meaning* directly, so we run each piece of text through a model that turns it
into a **vector** — a fixed-length list of numbers (for our models, 384 of them) that encodes its meaning.
That list is called an **embedding**.

The one rule that makes everything work: **texts with similar meaning get similar vectors.** "car" and
"automobile" land close together; "car" and "photosynthesis" land far apart.

*Analogy:* imagine every text is a point in a huge space. Similar meanings sit near each other, like books
on the same topic ending up on the same shelf.

### Measuring closeness: "cosine similarity"
To compare two vectors we look at the **angle** between them. Same direction = same meaning. This is
**cosine similarity**: `1.0` = identical direction, `0` = unrelated, `−1` = opposite.

Small detail you'll see in the code: we first make every vector length 1 ("**normalize**" it). Once
normalized, cosine similarity is just the **dot product** — multiply the two lists number-by-number and
add up the results. That's the `DotProduct(...)` function in `SearchAlgorithms.Vectors.cs`.

### Finding the closest ones: "kNN" and a "vector store"
- **kNN = k-Nearest Neighbours**: "give me the *k* vectors closest to my query vector" (k is just how
  many you want back, e.g. the top 50).
- Comparing your query against millions of vectors one at a time is slow. A **vector store** (a.k.a.
  vector **index**) organises the vectors so it can find the nearest ones fast. Kontext uses one called
  **USearch**. Think of it like the index at the back of a book — but for *meanings* instead of words.

This is the **semantic** ("by meaning") leg of search.

### Matching exact words: "keyword search", "FTS", and "BM25"
Meaning-search is fuzzy — it can miss an exact product code, a person's name, or a rare word. So we *also*
do classic keyword search.

- **FTS = Full-Text Search**: an index of which words appear in which documents. Kontext uses **Lucene**
  for this. Think of a library catalogue that can list every book containing the word "invoice".
- **BM25** is the standard formula FTS uses to *rank* keyword matches. You don't need the math — just read
  **BM25 = "how well do the keywords match"** (rare words that appear often in a document score high;
  long documents don't get to cheat by sheer length). This is the **lexical** score.

This is the **lexical** ("by exact words") leg of search.

### Why do both? "Hybrid search"
Semantic search understands meaning but blurs exact tokens; keyword search nails exact tokens but misses
synonyms and paraphrase. Running **both and merging them** is called **hybrid search**, and it's more
robust than either alone. (Example: searching "car" — semantic also finds "automobile"; keyword still
guarantees the literal word "car" isn't missed.)

### Merging two ranked lists: "RRF"
Now we have two candidate lists — one from keywords, one from meaning — each in its own order, with scores
on **totally different scales** (a BM25 score and a cosine score aren't comparable). How do you combine
them fairly?

**RRF = Reciprocal Rank Fusion.** It throws away the raw scores and looks only at **rank (position)**.
Each list "votes" for its entries: being #1 in a list is worth `1/(k + 1)`, #2 is worth `1/(k + 2)`, and
so on (`k` is a small constant that keeps the very top from dominating). Add up the votes across both
lists. Anything **both** lists rank highly collects the most votes and rises to the top.

*Analogy:* two judges each rank the contestants. You don't average their raw point totals (different
scales) — you reward whoever *both* judges placed near the top. Code: `AccumulateRrfScores` +
`NormalizeRrfScores` in `SearchAlgorithms.Rrf.cs`.

### Re-checking the finalists: the "cross-encoder" (a "reranker")
Each text's vector is computed **once, on its own**, ahead of time. Fast — but that vector never actually
"sees" your specific query next to the document. A model that encodes things separately like this is
called a **bi-encoder**.

A **cross-encoder** is a second, more accurate model that takes the **query and one candidate together**
and reads them *jointly* to score how relevant that candidate is to *this* query. Much smarter — but *much*
slower (it can't be pre-computed or indexed), so you only run it on the top few dozen candidates. Using it
to reshuffle the shortlist is called **reranking**.

*Analogy:* bi-encoder = matching people to a job by pre-written résumé summaries; cross-encoder = actually
interviewing the top few candidates with the job description in hand. You interview the shortlist, not
everyone.

### Trimming near-duplicates: "MMR"
The top results by relevance are often near-identical — five events that all say almost the same thing.
**MMR = Maximal Marginal Relevance** picks results that are relevant **and** different from what's already
been picked.

For each candidate it computes `λ × relevance − (1 − λ) × (similarity to what we've already chosen)`. The
dial **λ (lambda)** trades off "most relevant" vs "most diverse." So it takes the best result, then the
best result that *isn't a rerun* of it, and so on.

*Analogy:* building a playlist — you want great songs, but not the same song five times. Code:
`MmrSelect` in `SearchAlgorithms.Mmr.cs`.

### The backup yardstick: "Jaccard similarity"
MMR needs to measure "how similar are these two results?" to spot duplicates. Ideally it uses the
embeddings (cosine again). If embeddings aren't available, it falls back to **Jaccard similarity**: of the
distinct words in the two texts, what fraction do they share? `shared ÷ total-distinct`. `0` = no words in
common, `1` = identical word sets. Crude but good enough as a fallback. Code: `TokenJaccard` in
`SearchAlgorithms.Tokens.cs`.

### One more word: "hydrate"
During search we shuffle around lightweight candidate **IDs** (each ID is just *where* the event lives in
KurrentDB's log). **Hydrating** means fetching the *full* event — its data, type, timestamp — from the
database, but only once we've narrowed the field. It keeps us from lugging full event bodies around until
we actually need them.

---

## Part 2 — the pipeline, start to finish

This is what `KontextService.RunSearchAsync` does on each query. Inputs: `keywords` (words to match, where
a `~word` means "exclude this word") and an optional natural-language `query` (used only for the smart
rerank step).

**Stage 0 — Prepare the query**
- Split the keywords into *include* and *exclude* (`~`) terms.
- Pull out the meaningful phrases with an NLP step (noun-phrase extraction, via a library called Catalyst)
  → these become the words for keyword search.
- **Embed the query** into a vector (this is where the embedding model lives). Queries get a `"query:"`
  prefix because some models want to know "this is a search query, not a stored document."

**Stage 1 — Hybrid retrieval (get a big pool of candidates)** — `Retriever.HybridSearch`
- Run **BM25 keyword search** (Lucene) → a ranked list.
- Run **vector kNN** (USearch) with the query vector → another ranked list.
- **Fuse the two lists with RRF** → one merged, ranked candidate pool. (Remember: RRF merges by *position*,
  which is why two incompatible score scales can be combined.)
- *(For "recall" over memories, it also searches a separate stream-name index and RRF-folds those in.)*

**Stage 2 — Hydrate + filter** — `HydrateCandidates`
- For each candidate ID, read the real event from KurrentDB's transaction log.
- Drop any that don't pass the stream/event-type/exclude-word filters. What survives keeps its order.

**Stage 3 — Rerank the shortlist (only if a natural-language `query` was given)**
- Feed `(query, event-text)` pairs to the **cross-encoder** → a smarter relevance ranking of the shortlist.
- Merge that with the Stage-1 ranking using **RRF again** (so a result that both the hybrid retrieval *and*
  the cross-encoder liked is rewarded). This is a "two-stage" fusion.

**Stage 4 — Attach diversity signal**
- Pull each surviving candidate's embedding (for the next step). If there's no vector store, fall back to
  extracting each document's word set for **Jaccard**.

**Stage 5 — MMR: pick the final, diverse top-K**
- Use **MMR** to choose the final results (default is the top handful), balancing relevance against
  not-being-a-duplicate. Similarity between results is measured by embedding dot-product (cosine), or
  Jaccard if there are no embeddings.

**Result:** the top-K events, each returned as a `SearchHit` (stream, event number, type, timestamp, data,
and a rounded relevance score).

---

## Part 3 — a worked example

Query: keywords `database crash`, natural-language query `"what caused the outage last night?"`

1. **Prepare:** keywords → `["database", "crash"]`; embed `"query: database crash"` → a 384-number vector.
2. **Hybrid retrieval:**
   - BM25 finds events literally containing "database"/"crash".
   - kNN finds events that *mean* the same even without those words — e.g. "the server went down", "outage".
   - RRF merges both lists; an event that shows up high in *both* (say one titled "DatabaseOutage") ranks top.
3. **Hydrate + filter:** load those candidate events from the log; drop any excluded/filtered ones.
4. **Rerank:** the cross-encoder reads `"what caused the outage last night?"` next to each candidate and
   promotes the ones that actually explain a cause; RRF blends this with the Stage-1 order.
5. **MMR:** if the top three are three near-identical "DatabaseOutage" events, MMR keeps the best one and
   swaps the others for different-but-relevant events (a related alert, a root-cause note).
6. **Return** the diverse top-K.

---

## Part 4 — where the embedding *model* fits (the piece we keep comparing)

The embedding model (e.g. `all-MiniLM`, `multilingual-e5-small`, `paraphrase-multilingual-MiniLM`) is
**one component**, used in two of the five stages:
- **Stage 1**, the **vector-kNN leg** — a better model means better "by meaning" recall.
- **Stage 5**, the **MMR diversity** measure — a better model means cleaner de-duplication.

It is **not** the whole system. The **BM25** leg catches exact keywords/names the embeddings blur, and the
**cross-encoder** does the precision heavy-lifting on the shortlist. So choosing a better embedding model
improves the semantic + diversity parts; the lexical and rerank parts are independent of that choice.

This is also why comparing models with raw cosine on word pairs is only a *proxy*: in the real engine that
cosine is just the input to the vector leg, whose ranks are then fused (RRF) with BM25 and reshuffled by
the cross-encoder.

---

## The whole flow at a glance

```
                 query ("database crash" + "what caused the outage?")
                                   |
                    ┌──────────────┴───────────────┐
              keyword terms                    query vector
                    |                               |
             BM25 / Lucene FTS               vector kNN / USearch      ← Stage 1
             (exact words)                   (meaning)
                    └──────────────┬───────────────┘
                              RRF fusion            (merge two ranked lists by position)
                                   |
                          candidate pool
                                   |
                      hydrate + filter events        ← Stage 2 (load real events from the log)
                                   |
                   cross-encoder rerank (+ RRF)       ← Stage 3 (smarter, on the shortlist only)
                                   |
                    MMR: relevant AND diverse          ← Stage 5 (drop near-duplicates)
                                   |
                            top-K results
```

---

## Glossary (quick reference)

| Term | Plain meaning |
|---|---|
| **Embedding / vector** | A list of numbers representing a text's meaning; similar meaning → similar numbers. |
| **Cosine similarity** | How aligned two vectors are (1 = same meaning, 0 = unrelated). = dot product when normalized. |
| **Normalize** | Rescale a vector to length 1, so only its *direction* (meaning) matters. |
| **kNN** | "k nearest neighbours" — find the k closest vectors to the query. |
| **Vector store / index (USearch)** | Structure that makes kNN fast over many vectors. |
| **FTS (Lucene)** | Full-text (keyword) index — which words are in which documents. |
| **BM25** | The scoring formula for keyword-match relevance (the lexical score). |
| **Hybrid search** | Combining semantic (vector) + lexical (keyword) search. |
| **RRF** | Reciprocal Rank Fusion — merge ranked lists by position, not score. |
| **Bi-encoder** | Model that embeds query and document *separately* (fast, our embedding models). |
| **Cross-encoder / reranker** | Model that scores query + document *together* (accurate, slow, used on the shortlist). |
| **MMR** | Maximal Marginal Relevance — pick results that are relevant *and* non-redundant. |
| **λ (lambda)** | MMR's dial between "most relevant" and "most diverse". |
| **Jaccard** | Word-overlap similarity (shared ÷ total-distinct) — MMR's fallback when no embeddings. |
| **Hydrate** | Fetch the full event from the database once candidates are narrowed. |

## Where to read the code
- `src/KurrentDB.Kontext/Search/KontextService.cs` — the orchestrator (`RunSearchAsync`, all five stages).
- `src/KurrentDB.Kontext/Search/Retriever.cs` — Stage 1 hybrid search (BM25 + kNN → RRF).
- `src/KurrentDB.Kontext/Search/SearchAlgorithms.Rrf.cs` — RRF fusion.
- `src/KurrentDB.Kontext/Search/SearchAlgorithms.Mmr.cs` — MMR diversity selection.
- `src/KurrentDB.Kontext/Search/SearchAlgorithms.Vectors.cs` / `.Tokens.cs` — dot-product cosine / Jaccard.
- `src/KurrentDB.Kontext/Search/CrossEncoderService.cs` — the cross-encoder reranker (Stage 3).
- The embedding model that feeds Stage 1 lives in `src/KurrentDB.Kontext.Embeddings/` (see the migration
  report at `.claude/context/docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md`).
