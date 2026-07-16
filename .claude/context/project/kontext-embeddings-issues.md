# Existing Embeddings — Known Issues

> Companion to [`kontext-retrieval-pipeline.md`](./kontext-retrieval-pipeline.md). This catalogs the
> problems in the **current (default/"Prototype") local embedding path** — `EmbeddingService` +
> `WordPieceTokenizer` + `ModelManager` running `all-MiniLM-L6-v2`. Written junior-friendly (see the
> pipeline doc's glossary for any unfamiliar term). Each issue lists what it is, the evidence, why it
> matters, and its status. Deeper analysis + the migration plan live in
> [`docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md`](../docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md).
> Living doc — update as issues are fixed. Last reviewed 2026-07-13.

## The one-paragraph version
The existing embeddings have **three separate kinds of problem**: (1) the **tokenizer** — the part that
turns text into numbers the model understands — is hand-rolled and skips steps the model was trained with,
so it silently mangles accented and non-English text; (2) the **model** itself (`all-MiniLM`) is
English-only, so even with a perfect tokenizer it can't do Portuguese/Spanish/French/Japanese/Chinese well;
and (3) the **plumbing** around the model (how it's selected, loaded, and shipped) is prototype-grade.
Crucially, several of these fail **silently** — no error, just quietly worse results.

## Severity at a glance
| # | Issue | Layer | Severity | Status |
|---|---|---|---|---|
| 1 | Accents silently mangled (`café` → junk) | tokenizer | **High** | fixed by new **B** |
| 2 | Non-English is broken in practice | model | **High** | fixed by new **C** (multilingual) |
| 3 | Non-Latin `[UNK]`-collapse → *fake* matches | tokenizer | Medium-High | fixed by B/C |
| 4 | Hand-rolled tokenizer diverges from standard BERT | tokenizer | Medium | fixed by B/C |
| 5 | ICU / `InvariantGlobalization` silent-failure landmine | deploy | **High** | must-gate (applies to the fix too) |
| 6 | `ModelManager` AVX variant-selection is x86-centric cruft | infra | Low-Med | open (baseline kept as-is) |
| 7 | `Assembly.Load` reflection → AOT-hostile | infra | Medium | open (baseline kept) |
| 8 | Model distribution is prototype-grade | infra | Medium | open (design decision pending) |
| 9 | Cross-encoder shares the same weak tokenizer | ripple | Medium | open |

---

## Layer 1 — Tokenizer issues (the hand-rolled `WordPieceTokenizer`)

### 1. Accents are silently mangled — the headline bug
- **What:** the tokenizer does **no Unicode normalization / accent stripping**. `all-MiniLM` is an
  *uncased* BERT model, which was trained on text that is lowercased **and** has accents removed
  (`café` → `cafe`). The hand-rolled tokenizer skips that, so it looks up `café` verbatim, doesn't find
  it, and shatters it into `[UNK]` ("unknown") fragments — a token sequence the model never learned.
- **Evidence (measured):** cosine similarity of `café` vs `cafe` is **0.46** (should be ~1.0). `Müller`
  vs `Muller` was **0.12** — i.e. treated as basically unrelated.
- **Why it matters:** any memory with accented words — names (*José*, *Müller*, *São Paulo*), or any
  Romance-language text — gets a near-garbage vector, so a search **silently fails to find it**. No error;
  recall just quietly degrades.
- **Status:** **fixed** by the new implementation **B** (`WordPieceOnnxEmbeddingGenerator`), which uses a
  correct tokenizer and folds accents (`café`≈`cafe` → **1.0**). The Prototype keeps the bug on purpose —
  it's the frozen baseline (its vectors are already in existing indexes).

### 3. Non-Latin text collapses to identical `[UNK]` — *fake* matches
- **What:** for scripts the vocab doesn't cover well (CJK), distinct inputs can tokenize to the **same**
  `[UNK]` sequence, producing an artificial similarity of `1.0`.
- **Evidence:** `医者`/`医師` (two different Japanese words for "doctor") and `汽车`/`轿车` (Chinese) came out
  at cosine `1.0000` under the baseline — not real understanding, just both collapsing to `[UNK]`.
- **Why it matters:** worse than missing a match — it *invents* matches. Unrelated non-Latin texts can look
  identical, so retrieval can confidently return the wrong memory.
- **Status:** fixed by B/C (proper tokenization; C actually understands the scripts).

### 4. The hand-rolled tokenizer is non-standard
- **What:** it uses case-insensitive dictionary lookups, no Unicode normalization, .NET's
  `char.IsPunctuation` (not BERT's punctuation rules), and a 200-char max word (HF uses 100). These are
  small deviations from canonical BERT WordPiece that produce subtly different tokens on edge cases.
- **Why it matters:** even on text that "works," the vectors can drift from what the model expects,
  lowering quality in hard-to-notice ways.
- **Status:** fixed by B/C (they use maintained, HF-faithful tokenizers — `Microsoft.ML.Tokenizers`).

### 5. ICU / `InvariantGlobalization` — a silent-failure landmine (applies to the fix, too)
- **What:** correct accent-stripping relies on `String.Normalize(FormD)`, which needs **ICU** (the
  Unicode library). If the process runs with **`InvariantGlobalization=true`** (set in several repo
  projects) or on an image without ICU (e.g. **Alpine/musl**), `String.Normalize` becomes a **silent
  no-op** — no exception — and the accent bug reappears even with the correct tokenizer.
- **Evidence:** reproduced — under invariant globalization, `café` fails to fold to `cafe` and returns to
  `[UNK]` behavior.
- **Why it matters:** it's invisible. A correct fix can be silently defeated by a deployment setting, and
  you'd only notice as "search feels worse."
- **Status:** **must-gate.** The new code adds a fail-loud startup check (asserts `café`→`cafe`) so it
  throws instead of degrading silently. The shipping image must have ICU (`InvariantGlobalization=false`).

---

## Layer 2 — Model issue (`all-MiniLM-L6-v2` is English-only)

### 2. Non-English is broken in practice
- **What:** `all-MiniLM` is an English model. This is **separate from the tokenizer bug**: even with a
  *perfect* tokenizer, the model has weak semantics for non-English text.
- **Evidence (measured, within-language synonym vs unrelated separation — higher is better):** with the
  *correct* tokenizer, Portuguese separation was **−0.09** (worse than random — it rated synonyms *below*
  unrelated words), Spanish ~0.03, French ~0.06. English was fine (~0.58). So the model, not just the
  tokenizer, fails on other languages.
- **Why it matters:** for a memory store that will hold arbitrary user content (names, non-English notes),
  the default model is effectively English-only.
- **Status:** **fixed** by the new implementation **C** (`SentencePieceOnnxEmbeddingGenerator`) with a
  multilingual model. Best current pick from our eval: **`paraphrase-multilingual-MiniLM-L12-v2`** (clean
  within-language separation across all 7 target languages, same 384-dim as the baseline). Note e5-family
  models scored *worst* on within-language separation despite being largest — see the report.

---

## Layer 3 — Infrastructure issues (`ModelManager` + distribution)

### 6. AVX variant-selection is x86-centric cruft
- **What:** `ModelManager` ships **two** INT8-quantized copies of each model (`…avx512` and `…avx2`) and
  picks one by CPU feature. This roughly doubles the embedded payload (the models assembly is ~53 MB).
- **Why it's questionable:** ONNX Runtime already picks optimal CPU kernels itself; and on **ARM**
  (Apple Silicon, ARM servers — which KurrentDB ships images for) `Avx512F.IsSupported` is false, so it
  silently loads the model literally named `avx2` — a meaningless, x86-centric choice on ARM.
- **Status:** open. It's baseline code we're keeping as-is; flagged as a simplification opportunity
  (ship one model, let ORT dispatch), **not** being changed now.

### 7. `Assembly.Load` reflection → AOT-hostile
- **What:** `ModelManager` loads the embedded model bytes via `Assembly.Load("KurrentDB.Kontext.Models")`
  + `GetManifestResourceStream` — reflection-flavored loading.
- **Why it matters:** the repo targets **Native AOT** and bans reflection; this is an AOT smell that
  fights that goal.
- **Status:** open (baseline kept). The new project avoids embedding-in-assembly, taking model **streams**
  instead, so the fix path sidesteps this.

### 8. Model distribution is prototype-grade
- **What:** models are downloaded from HuggingFace **at build time** (`DownloadFile` MSBuild target) and
  **embedded into a DLL** as resources (~53 MB), then loaded from resources at runtime.
- **Why it matters:** it bloats the binary, requires network **at build**, has **no integrity/SHA check
  and no versioning**, and you can't update a model without a rebuild.
- **Status:** open — a design decision is pending (see the report's "Model distribution" section:
  runtime download-to-cache with a pinned checksum + a configurable models directory, so the binary stays
  lean and air-gapped installs can pre-seed).

---

## Ripple

### 9. The cross-encoder shares the same weak tokenizer
- **What:** `CrossEncoderService` (the reranker — see the pipeline doc, Stage 3) uses the *same*
  hand-rolled `WordPieceTokenizer` (its `EncodePair` path).
- **Why it matters:** all the tokenizer problems above (accents, `[UNK]`-collapse) also degrade the
  **rerank** step for non-English content — so the issue isn't confined to the first-stage vectors.
- **Status:** open — must be migrated to a correct tokenizer alongside the embedding change.

---

## How to think about it (summary)
- **Tokenizer bugs (1, 3, 4, 5)** → fixed by using a maintained tokenizer (implementation **B**), *plus*
  the ICU deployment gate.
- **Model limitation (2)** → fixed by adopting a multilingual model (implementation **C**, e.g.
  `paraphrase-multilingual-MiniLM-L12`).
- **Infrastructure (6, 7, 8)** → known, mostly left on the frozen baseline; the new project's design avoids
  them, and model-distribution is an open decision.
- **Ripple (9)** → the cross-encoder needs the same tokenizer fix.

The single most important takeaway: **most of these fail silently.** The baseline doesn't throw on a
mangled accent or a non-English query — it just returns worse results. That's why they went unnoticed and
why the new code adds explicit fail-loud checks.
