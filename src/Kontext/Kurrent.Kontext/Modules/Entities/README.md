# Entity memory, end to end

Three pipelines: a synchronous write path that only takes cheap, high-confidence identity decisions, an offline resolution channel for everything it deferred, and a read path that prices unresolved doubt in so deferring stays safe. Node names match the classes.

```mermaid
flowchart TD

subgraph WRITE["1 · WRITE PATH, synchronous, one ordered projector loop"]
    A["MemoriesRetained batch arrives<br/>KontextEntityProjectorService"] --> B["Extract entities from each memory<br/>EntityExtractionPipeline: Catalyst NER ∪ regex patterns,<br/>junk gate EntityName.IsValid"]
    B --> C["Group occurrences per normalized name + type,<br/>best confidence wins"]
    C --> D["Embed group names, one batched model call"]
    D --> E{"Decision rule, per candidate<br/>EntityDeduplicator over Exact → Fuzzy → Semantic"}
    E -->|"exact or score ≥ 0.95, certain"| F["Merge into match,<br/>spelling saved as alias"]
    E -->|"0.85 ≤ score < 0.95, unsure"| G["Don't guess: new entity<br/>plus a pending link, the note"]
    E -->|"score < 0.85, different"| H["New entity"]
    F --> W1["KontextEntityWriter applies the delta:<br/>mentions first, counts recounted never incremented,<br/>idempotent under replay"]
    G --> W1
    H --> W1
end

subgraph STORE["THE STORE, three lance tables, KontextEntitySchemaTask"]
    T1[("entities<br/>canonical name, aliases, type,<br/>mention_count, embedding")]
    T2[("entity_mentions<br/>append-only provenance:<br/>which memory said which name where")]
    T3[("entity_links<br/>status pending = unresolved doubts,<br/>confidence, method")]
end

W1 --> T1
W1 --> T2
W1 --> T3

subgraph RES["2 · RESOLUTION, asynchronous, runs only when called, no scheduler"]
    R1["KontextEntityResolutionService<br/>lists the queue oldest first with survivor preview,<br/>refuses when no projector runs"] --> R2["EntityWriteGate<br/>takes whole turns on the projector's write connection,<br/>a verdict waits out at most one in-flight batch"]
    R2 --> R3{"EntityVerdictExecutor<br/>survivor = more mentions, tie earlier first_seen,<br/>explicit override allowed"}
    R3 -->|"same thing"| R4["Merge: refile mentions, fold aliases,<br/>recount, repoint other links,<br/>delete loser, link → confirmed"]
    R3 -->|"different"| R5["Link → rejected,<br/>doubt never raised again"]
end

T3 -. "review queue" .-> R1
R4 --> T1
R4 --> T2
R4 --> T3
R5 --> T3

subgraph READ["3 · READ PATH, question time, KontextRetriever Default and Hybrid chains"]
    Q["Question text"] --> S1["Plan → vector + keyword search legs<br/>→ RRF fusion → BM25 reread"]
    S1 --> S2["CognitiveModulator<br/>recency, importance, certainty per memory"]

    subgraph EM["EntityModulator, pass-through when no entity store is wired"]
        E1["Recognize entities the question names:<br/>normalized surfaces vs names and aliases,<br/>no model calls, unprojected store skipped"]
        E1 --> E2["Rarity per entity<br/>w = 1 / (1 + 0.001·(n−1)²), n = mention_count"]
        E2 --> E3["Cross pending notes one hop, never two:<br/>neighbour w = rarity × link confidence × 0.5"]
        E3 --> E4["Per candidate memory<br/>signal = 1 − Π(1 − wᵢ) over its mentioning entities"]
        E4 --> E5["Nudge score × clamp(1 + 0.1·signal, 1.00, 1.10)<br/>zero signal = exactly ×1.00, never demotes"]
    end

    S2 --> E1
    E5 --> S3["MmrReorderer, diversity"]
    S3 --> S4["SeatAllocator<br/>per-kind share caps, spares to uncapped kinds,<br/>capped kinds never re-enter, default no caps"]
    S4 --> S5["CutStep, limit + min score"]
    S5 --> OUT["top-N memories to the agent"]
end

T1 -. "names, aliases, mention_count" .-> E1
T3 -. "pending notes" .-> E3
T2 -. "entity → memory" .-> E4
```

Worked example: "Emilia Chen" arrives, scores 0.84 against Emily Chen. Too close to ignore, not enough to merge, so both entries exist joined by a pending note. A question naming Emily pushes Emily's memories at ×1.10 and, across the note, Emilia's at ×1.042 (1 + 0.1 · (1.0 × 0.84 × 0.5)). The doubt costs almost nothing while it waits. Later a human confirms the typo through the resolution service and the entries merge for good, with "emilia chen" kept as an alias so the next occurrence resolves exactly, for free.

## Where each stage lives

| Stage | Code |
|---|---|
| Projector loop | `KontextEntityProjectorService.cs` |
| Extraction cascade | `Extraction/` (`EntityExtractionPipeline`, `CatalystEntityExtractor`, `PatternEntityExtractor`, `EntityName`) |
| Grouping, embedding, branch handling | `KontextEntityProjection.cs` |
| Decision rule, thresholds | `Resolution/EntityDeduplicator.cs` over `ExactEntityResolver`, `FuzzyEntityResolver`, `SemanticEntityResolver` |
| Batch writer | `Data/KontextEntityWriter.cs` |
| Tables, indexes | `KontextEntitySchemaTask` (v2 of the migration stream, `../../KontextSchema.cs`) |
| Store reads | `Data/KontextEntityStore.cs` |
| Resolution surface | `Resolution/KontextEntityResolutionService.cs` |
| Projector/resolution serialization | `Resolution/EntityWriteGate.cs` |
| Verdict executor | `Resolution/EntityVerdictExecutor.cs` |
| Read chain | `../../../Kurrent.Kontext.Retrieval/KontextRetriever.cs` |
| Entity nudge stage | `../../../Kurrent.Kontext.Retrieval/Stages/EntityModulator.cs`, port `../../../Kurrent.Kontext.Retrieval/Search/IEntityIndex.cs`, adapter `KontextEntityIndex.cs` |
| Seat caps | `../../../Kurrent.Kontext.Retrieval/Stages/SeatAllocator.cs` |
