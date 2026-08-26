---
name: kontext-scoring-config-not-logged
description: Agreed follow-up — MemoriesRecalled.config is left unset because ScoringConfig is only half reachable; needs an IKontextRetriever change
metadata: 
  node_type: memory
  type: project
  originSessionId: 61716747-5d17-4628-990c-5011709058cd
  modified: 2026-08-26T15:10:57.013Z
---

`KontextMemory.RecordRecallAsync` fills every per-hit number on `MemoriesRecalled` but leaves
`config` (`ScoringConfig`) unset. Agreed 2026-08-26 to fix later, not to skip.

**Why:** the field is only half reachable from where the event is built.

| Field group | Lives | Reachable |
|---|---|---|
| `alpha_recency` / `alpha_importance` / `alpha_relevance` / `recency_tau_seconds` / `importance_weights` | `CognitiveModulationOptions` on the modulator STAGE | only via a new property on `IKontextRetriever` |
| `recency_bounds` / `importance_bounds` / `relevance_bounds` | computed per candidate pool INSIDE the modulator run | only by returning them from `RetrieveAsync` |

`ScoringConfig` is therefore not purely config — the three `Bounds` are per-recall values, which is why
no settings object can supply it.

**How to apply:** change `IKontextRetriever.RetrieveAsync` to return a result object carrying the
memories plus the run's config, rather than a bare `IReadOnlyList<ScoredMemory>`. One implementation
(`KontextRetriever`) plus the retrieval tests. Sérgio's principle for events applies: recording costs
little and replays, omitting is permanent — see [[kontext-v3-contract-state]].
