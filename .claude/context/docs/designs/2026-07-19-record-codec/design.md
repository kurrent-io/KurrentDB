---
title: RecordCodec — pluggable record mapping for DuckLance (Phase 0 + integration)
status: accepted
authors: [sergio]
date: 2026-07-19
tags: [ducklance, kontext, codec, mapping, vector]
related: [project/duckdb-lance.md]
---

## Context

`DuckDBRecordMapper<TRecord>` (model-driven, reflection-accessor based) is being replaced by a
pluggable codec abstraction so hot record types can hand-write direct, typed mapping while every
other type falls back to a model-driven default. The design was converged over a long session on
2026-07-18/19; every decision below is SETTLED — do not re-litigate, do not re-suggest dropped
alternatives. Trust order: this doc > memory files > conversation recollection.

**Read first:** `.claude/context/project/duckdb-lance.md` (the stack KB — wire shapes, positional
facts, failure modes), `.claude/memory/sergio-csharp-style-law.md` (mandatory style rules),
`.claude/memory/kontext-kurrentdb-integration-exploration.md` (decision log).

## Current state on disk (all built, tested, suite 444/444, NOT integrated)

- `src/Kurrent.Kontext.DuckLance/Mapping/RecordCodec.cs` — SINGLE-vector base (GetVectorText,
  `Encode(record, float[]?)`, `Decode(reader, includeVectors)`, virtual vectorize single+batch,
  optional generator with loud throws, `ToWireVector`). **To be reshaped per Target Design below.**
- `src/Kurrent.Kontext.DuckLance/Mapping/DuckDBModelCodec.cs` — model-driven codec; currently
  THROWS on multi-vector models in ctor (to be removed); positional decode via layouts precomputed
  in ctor (full + lean); coercion logic ported from the mapper (blob-as-stream, UTC timestamps,
  list/array materialization, DBNull semantics).
- `src/Kurrent.Kontext.DuckLance/Mapping/Experiments.cs` — `MyMemoryEntry` + `MemoryCodec`
  (the 12-line hand-written reference).
- `src/Kurrent.Kontext.DuckLance.Tests/Mapping/DuckDBModelCodecTests.cs` — 11 tests (encode
  pass-through-by-reference, positional full decode, lean-shift with vector mid-model, DBNull,
  DateTimeOffset UTC coercion, blob-from-stream, dynamic records, multi-vector ctor rejection
  [flips to a positive test in Phase 0], native extraction without generator, base no-generator throw).
- `src/Kurrent.Kontext.DuckLance.Tests/Support/FakeDbDataReader.cs` — shared reader fake
  (promoted out of the mapper tests).
- `DuckDBRecordMapper` + `DuckDBRecordMapperTests` — UNTOUCHED, still the wired production path.

## Target design (agreed API — implement exactly this shape)

Slot concept: a record has one vector slot per vector COLUMN. Slots are addressed BY NAME
(storage column name), never by position — the vector↔column association must be written in code
at both ends, not implied by ordering.

```csharp
/// What to embed, and which vector COLUMN it is for.
public readonly record struct VectorText(string Column, string Text);

/// The wire-ready vectors for ONE record, addressed by column name.
/// Implementers CONSUME this; only the base and the pipeline CONSTRUCT it (internal ctor/factory).
public readonly struct VectorSlots {
    public int      Count               { get; }
    public float[]? this[string column] { get; }  // named access (linear scan; 1-3 entries)
    public float[]? Single              { get; }  // the only slot (Count <= 1); THROWS when Count > 1
}

public abstract class RecordCodec<TRecord>(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    where TRecord : notnull {
    protected IEmbeddingGenerator<string, Embedding<float>>? EmbeddingGenerator { get; }

    /// One entry per vector column: (storage column name, generation text).
    protected abstract VectorText[] GetVectorTexts(TRecord record);

    /// One value per column, in model-property order — values[i] IS column i (THE LAW).
    public abstract object?[] Encode(TRecord record, VectorSlots vectors);

    /// Reads THE CURRENT ROW, positionally; the caller drives the loop. Lean projections omit
    /// vector columns, shifting later positions.
    public abstract TRecord Decode(DbDataReader reader, bool includeVectors);

    /// Default: generator over GetVectorTexts. Wire-ready named output.
    public virtual ValueTask<VectorSlots> VectorizeAsync(TRecord record, CancellationToken ct = default);

    /// One VectorSlots per record, input order. Internally ONE generator call (flatten texts
    /// across records×slots, reshape). NEVER expose jagged arrays on any surface.
    public virtual ValueTask<VectorSlots[]> VectorizeBatchAsync(IReadOnlyList<TRecord> records, CancellationToken ct = default);

    // embedding→wire conversion is a local function inside the vectorize defaults (Sérgio's
    // Phase 0 review: not public API); the model codec reuses its own ExtractVector instead.
}

/// The 99% tier — exactly the current three-member experience. Sealed routing; single-vector
/// authors never see VectorSlots/VectorText/column names.
public abstract class SingleVectorRecordCodec<TRecord>(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    : RecordCodec<TRecord>(embeddingGenerator) where TRecord : notnull {
    protected abstract string    GetVectorText(TRecord record);
    public abstract    object?[] Encode(TRecord record, float[]? vector);

    protected sealed override VectorText[] GetVectorTexts(TRecord r)        => [new("", GetVectorText(r))]; // one anonymous slot
    public sealed override    object?[]    Encode(TRecord r, VectorSlots v) => Encode(r, v.Single);
}
```

Multi-vector reference (for docs/tests — shows the named association at both ends):

```csharp
public class ArticleCodec(IEmbeddingGenerator<string, Embedding<float>> embedder) : RecordCodec<Article>(embedder) {
    protected override VectorText[] GetVectorTexts(Article r) => [new("title_vec", r.Title), new("body_vec", r.Body)];
    public override object?[] Encode(Article r, VectorSlots v) => [r.Id, r.Title, v["title_vec"], r.Body, v["body_vec"]];
    public override Article Decode(DbDataReader reader, bool includeVectors) => …;
}
```

`DuckDBModelCodec<TRecord>` extends the ROOT directly:
- ctor throw on multi-vector REMOVED; `_vectorProperties` (with model indices) replaces the single;
  the existing lean-layout loop already generalizes (marks every vector property -1) — keep it.
- Overrides plural `Encode` as the real implementation (each slot at its property's position, looked
  up by storage name); no single-vector members (it is not on the SingleVector tier).
- Vectorize routes PER SLOT: native-typed property (ROM<float>/Embedding<float>/float[]) → extract
  synchronously, no generator; generation-input property (e.g. string) → **MEVD's own per-property
  dispatcher** (`vectorProperty.GenerateEmbeddingAsync`) — NOT the codec-held generator. This
  preserves all MEVD generation features (per-property generators, non-string inputs) for free and
  keeps `DuckDBEmbeddingGenerationTests` untouched. Mixed native+text models need no special-casing.
- `GetVectorTexts` for the model codec is vestigial (vectorize fully overridden) — implement as a
  clear throw.

## Settled decisions (do NOT reopen; each was explicitly ruled by Sérgio)

1. **No `TInput` type parameter** on codecs. Exotic generation inputs = override VectorizeAsync
   (image encoders etc.) or adapt at the generator boundary. Revisit trigger: DuckLance becomes a
   published package (same trigger re-adds interfaces + sealed vectorize — "package hardening").
2. **Vectors pinned to `float`** end-to-end. DuckDB has no FLOAT16 type; the column is FLOAT[N].
   Half/int8 generators get widening adapters at the IEmbeddingGenerator boundary.
3. **Multi-vector IS supported** via named slots (Sérgio reversed an earlier single-vector-only
   ruling after learning the mapper already supports it — `DuckDBMultiVectorTests` must keep
   passing through the codec after integration).
4. **Resolution rule**: registered codec wins, `DuckDBModelCodec` is the unconditional default,
   resolution never fails or asks.
5. **Wire shapes** [validated]: binds accept BOTH `T[]` and `List<T>`; reads ALWAYS return
   `List<T>` (driver's choice). No normalization on the write path — pass collections through.
6. **Positional law** (both directions): the composers emit columns in `model.Properties` order
   (vector columns omitted on lean projections); search SELECTs append `_distance`/`_hybrid_score`
   AFTER record columns, so record positions always hold. No per-query ordinal work anywhere.
7. Naming: `Encode`/`Decode`; `SingleVectorRecordCodec` (long-but-clear beats short-but-vague);
   junior-readable comments everywhere (see style-law memory: summary-only docs on non-public,
   local functions, modern C#14/net10 idioms, no NRT-dead guards).

## Phase plan

**Phase 0 — reshape to the target design** (then STOP for Sérgio's review):
- Add `VectorText`, `VectorSlots` (construction internal to assembly).
- Reshape `RecordCodec` root; add `SingleVectorRecordCodec`; rebase `MemoryCodec` (one token).
- `DuckDBModelCodec`: multi-vector + per-slot vectorize per above.
- Tests: `VectorSlots` semantics (0/1/N, `Single` throw, named access), multi-vector model
  encode/decode round-trip (flip the ctor-rejection test), mixed native+text vectorize, batch
  flatten/reshape correctness. Full suite green.

**⏸ REVIEW GATE — Sérgio looks, then says proceed.**

**Phase 1 — registry**: `Codecs` on `DuckDBVectorStoreOptions` (`Add<TRecord>(RecordCodec<TRecord>)`,
internal `Resolve<TRecord>()`); contents copied by the defensive options copy-ctor.

**Phase 2 — collection swap** (`DuckDBCollection`):
- ctor: `_codec = options.Codecs.Resolve<TRecord>() ?? new DuckDBModelCodec<TRecord>(Model)`.
- 5 read loops: drop `_mapper.ResolveOrdinals(...)` lines; `_mapper.MapFromReader(...)` →
  `_codec.Decode(reader, includeVectors)`. Sites ≈ lines 254, 652, 694, 768, 1136 (drifts; grep
  `MapFromReader`).
- `DoUpsertAsync`: DELETE the MEVD `generatedEmbeddings` dictionary machinery entirely →
  `var slots = await _codec.VectorizeBatchAsync(uniqueRecords, ct)` then
  `_codec.Encode(record, slots[i])` per record. (Vector-less models: guard on
  `Model.VectorProperties.Count == 0` → empty slots.)
- Query-side (search input) embedding stays on MEVD's existing machinery — unchanged.

**Phase 3 — retire the mapper**: port the mapper tests' scalar-type-matrix round-trip cases into
the codec tests, then delete `DuckDBRecordMapper.cs` + `DuckDBRecordMapperTests.cs`.
`DuckDBMultiVectorTests` now passes THROUGH the codec — nothing retired.

**Phase 4 — the gate**: full suite; the engine-level CRUD/search/sweep/multi-vector tests running
through the codec against real DuckDB+Lance are the parity proof, not the unit tests.

**Wrap-up**: update `project/duckdb-lance.md` (codec is the mapping layer) and the exploration
memory (integration state).

## Revision — 2026-07-19 (implementation landed)

All phases complete, same day. Phase 0 reviewed by Sérgio; one revision from that review:
`ToWireVector` is NOT public API — it lives as local functions inside the base vectorize defaults,
and `DuckDBModelCodec` reuses its own `ExtractVector` (identical conversion arm). Phases 1–4:
`Codecs` registry on the options (copy-ctor copies), collection swapped (5 read sites → `Decode`,
upsert → `VectorizeBatchAsync` + `Encode` with a vector-less-model guard), mapper + tests deleted
after porting the scalar-matrix round-trips. Full suite 454/454 including the engine-level parity
tests. Notable implementation choices: model codec ctor takes only the model (no generator — MEVD
dispatchers only); a vector-less model vectorizes to empty slots instead of throwing; `VectorSlots`
unknown-column access throws `KeyNotFoundException`, multi-slot `Single` throws
`InvalidOperationException`.

## Verification protocol

- NO test-runner script exists in this repo: `dotnet test src/Kurrent.Kontext.DuckLance.Tests/DuckLance.Tests.csproj -c Release`
  (~20 s incl. build). Redirect to a log and read the "Test run summary" block — NEVER trust a
  piped exit code.
- Known probabilistic flakes (rerun the class in isolation before suspecting a regression):
  `RealTimer_EventuallyCreatesTheIndexAndLandsTheAppend` (real 200 ms timer, timing-sensitive under
  parallel load) and `DuckDBMultiVectorTests`' IVF_HNSW_PQ recall check (ANN margins; documented in
  the test).
- Sérgio edits files concurrently in an IDE: on any Edit mismatch, re-read and retry against
  current content. Match each file's existing style exactly.

## Risks / tripwires

- MEVD's `CollectionModelBuilder` itself rejects string-typed vector properties when the MODEL has
  no generator — don't write tests that assume such a model can exist.
- `VectorSlots` must not be constructible by implementers (internal ctor) — mis-built slots are the
  one way to silently violate the named-association guarantee.
- The batch default must remain ONE generator call regardless of slot count (flatten/reshape in the
  base); degrading to N calls is a regression the tests must catch.
- Do not touch: composers, SQL text, `ldb` alias, DatabasePath convention, the ATTACH-race catch in
  `LanceConnectionPool.Initialize`, `Internal/VectorStoreErrorHandler.cs` (vendored shim),
  `Internal/Verify.cs` (dead but deletion is Sérgio's call, still pending).
