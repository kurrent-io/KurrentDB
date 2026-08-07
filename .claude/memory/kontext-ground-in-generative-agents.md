---
name: kontext-ground-in-generative-agents
description: Ground Kontext/Engram design reasoning in the Stanford Generative Agents paper
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a9e741eb-2837-4af3-9ac5-4b6fd8f094eb
---

When helping with Kontext (the KurrentDB.Kontext.V3 memory system, codename "Engram"; protos at `src/KurrentDB.Kontext.V3.Contracts/protos/kurrentdb/kontext/v3/memory/`), base design reasoning on the Stanford paper "Generative Agents: Interactive Simulacra of Human Behavior" (Park et al., 2023).

**Why:** Sérgio is deliberately modeling Kontext on that architecture — a memory stream where retrieval score = recency (exponential decay since *last access*) + importance (LLM-rated, here 0–1) + relevance (embedding similarity); periodic reflection (triggered when accumulated importance crosses a threshold; insights must cite their evidence memories); and planning. `MemoryType {observation, reflection, plan, hearsay}` maps to the paper's memory-object types.

**How to apply:** Cite the paper's actual mechanics, and explicitly flag where the design *extends* it — notably (1) the `HEARSAY` type: dialogue-derived claims are kept as unverified rather than logged as observations, closing the "memory hacking" hole where fabricated events get injected via conversation (the paper logs dialogue as observations); (2) event-sourcing; (3) `RecallSignal` and explicit `confidence` were removed in favor of temporal decay + memory-type trust. Recency refreshes on retrieval via `last_accessed_at`. See [[log-position-not-tfpos]] and [[no-unauthorized-scope-cuts]].
