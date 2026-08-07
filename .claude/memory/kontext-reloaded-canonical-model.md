---
name: kontext-reloaded-canonical-model
description: Kontext.Reloaded chose Path B — the gRPC canonical contract model IS the core (IKontextMemoryService); the decoupled domain was deleted; only the MCP edge still maps
metadata: 
  node_type: memory
  type: project
  originSessionId: 070fdb0c-0407-489f-a212-18609e9a0477
---

On 2026-07-16 the earlier **Path A** ("decoupled hand-rolled domain core") was **consciously reversed to Path B**.
Do NOT re-recommend decoupling — it was deliberately dropped (this supersedes the old `core-decoupled` note).

- `IKontextMemory` (namespace `Kurrent.Kontext`, file `IKontextMemory.cs`; renamed from the original
  `IKontextMemoryService`/`KurrentDB.Kontext.Reloaded`) is the **core service port** and speaks the gRPC canonical
  contract types (`Kurrent.Kontext.Contracts.*`) directly — request in, response out. "Changing the domain" now
  means changing the proto.
- **DELETED:** `Core/IKontextMemory` + all of `Core/Model/*` (the decoupled domain) and `Edges/Grpc/MemoryMappers.cs`.
  The gRPC edge `Edges/Grpc/GrpcMemoryService.cs` is a **pure pass-through** over `IKontextMemoryService` (no mapping).
- The **MCP edge is the only place mapping survives.** `Edges/Mcp/Model/*` is an HTTP-friendly copy of the old model
  (with `[Description]` for tool schemas) where identity VOs (`MemoryId`/`QueryId`) are **flattened to `string`**.
  `Edges/Mcp/McpMappers.cs` maps that model ⇄ `Contracts.*`; `Edges/Mcp/McpMemoryService.cs` maps HTTP model →
  contract request, calls `IKontextMemoryService`, and folds the contract response back into the model.
- Validation decorator (`Infrastructure/FluentValidation/KontextMemoryValidationDecorator`) wraps
  `IKontextMemoryService` over the canonical request types.
- Since built: DI registration (`AddKontext`), and (2026-07-17) a `KontextMemory` skeleton implementing
  `IKontextMemory` over the `Microsoft.Extensions.VectorData` `VectorStore` abstraction (10.7.0) — one "memories"
  collection, `MemoryRecord` row, filters confined to equality+Contains so the store is swappable (DuckDB spec).
- Still open (not built): `RequestValidationException` → edge translation (gRPC `InvalidArgument` / HTTP 400),
  the event log (retract reasons have no home), scoring beyond raw similarity, and `Reflect` (needs an LLM;
  throws NotImplementedException).

**Why:** the user made this a deliberate design choice ("conscious choice") — accept coupling the core to the
canonical model to eliminate the domain-mapping layer; the mapping cost moves to the MCP edge only.

**How to apply:** build Reloaded services against `IKontextMemoryService` + `Contracts.*`; never reintroduce a
separate domain model. See [[kontext-v3-contract-state]] for the proto shapes the canonical model is generated from.
