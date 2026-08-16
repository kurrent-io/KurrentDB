---
title: Kontext Document Ingestion — Microsoft.Extensions.DataIngestion, single core, gRPC + MCP edges
type: research            # research | spike | investigation
date: 2026-08-11
author: sergio
tags: [kontext, ingestion, dataingestion, mcp, grpc, rag, markdown, pdf]
---

# Research — Kontext Document Ingestion — Microsoft.Extensions.DataIngestion, single core, gRPC + MCP edges

## Question

Kontext needs a document-upload-and-process capability: one core implementation exposed over two
edges (MCP + gRPC), exactly like Kontext memory. Documents flow through
`Microsoft.Extensions.DataIngestion` — markdown first, PDF later. What does the library actually
provide, how does it fit the existing Kontext architecture, and what shape should the two edges
take?

All external facts below were verified in-session on 2026-08-11 (nuget.org, Microsoft Learn, MCP
spec, GitHub). Anything a research unit could not pin to a source is marked UNVERIFIED.

## Findings

### 1. The library — Microsoft.Extensions.DataIngestion

Home is `dotnet/extensions` (`src/Libraries/Microsoft.Extensions.DataIngestion*`), NOT `dotnet/ai`.
First preview 2025-11-11; announced on the .NET Blog 2025-12-03. Still preview after ~9 months,
monthly bumps tied to the .NET 10 SDK preview cadence. No GA signal.

| Package | Latest (2026-08-11) | TFMs | License |
|---|---|---|---|
| `Microsoft.Extensions.DataIngestion` | 10.8.0-preview.1.26364.2 | net8.0, netstandard2.0, net462 | MIT |
| `Microsoft.Extensions.DataIngestion.Abstractions` | 10.8.0-preview.1.26364.2 | net8.0–net10.0, netstandard2.0, net462 | MIT |
| `Microsoft.Extensions.DataIngestion.Markdig` | 10.8.0-preview.1.26364.2 | net8.0, netstandard2.0, net462 | MIT |
| `Microsoft.Extensions.DataIngestion.MarkItDown` | 10.8.0-preview.1.26364.2 | net8.0, netstandard2.0, net462 | MIT |

Version alignment with the repo is clean: the repo pins `Microsoft.Extensions.AI` 10.8.3 and
`Microsoft.Extensions.VectorData.Abstractions` 10.8.0 (`src/Directory.Packages.props`);
DataIngestion 10.8.0-preview sits on the same wave. `.MarkItDown` additionally depends on
`ModelContextProtocol.Core >= 1.2.0` (repo pins `ModelContextProtocol` 2.1.0).

**Pipeline model** (namespace `Microsoft.Extensions.DataIngestion`):

```
IngestionDocumentReader ──▶ IngestionDocument ──▶ IngestionChunker<T> ──▶ IngestionChunkWriter<T>
        │                   (sections tree,          │ (streams                  │ (consumes
        │ Task<IngestionDocument>                    │  IAsyncEnumerable         │  IAsyncEnumerable
        │ = one doc materialized)                    │  <IngestionChunk<T>>)     │  — streaming sink)
        │
   [IngestionDocumentProcessor]                [IngestionChunkProcessor<T>]
   doc-level enrichment                        chunk-level enrichers (IChatClient):
   (ImageAlternativeTextEnricher)              Summary / Sentiment / Keyword / Classification
```

- `IngestionPipeline<T>` (sealed, `IDisposable`) composes the stages;
  `ProcessAsync(DirectoryInfo, searchPattern)` yields `IAsyncEnumerable<IngestionResult>`
  (`DocumentId`, `Succeeded`) — per-document partial success; one failed doc does not fail the run.
- Chunkers: `HeaderChunker` (header-context preserving), `SectionChunker`, `DocumentTokenChunker`
  (overlapping token windows), `SemanticSimilarityChunker` (embedding-distance splits, costs
  embedding calls). All driven by `IngestionChunkerOptions(Tokenizer)` — `MaxTokensPerChunk`,
  `OverlapTokens` — tokenizers from `Microsoft.ML.Tokenizers`.
- `VectorStoreWriter<T>` is the one built-in writer: targets any MEVD `VectorStore`, embedding via
  the store's configured `IEmbeddingGenerator`, explicit `dimensionCount`, and an
  `IncrementalIngestion` option with per-document replace semantics (insert new chunks, delete the
  document's old ones).
- **Custom extension seams are abstract classes, not interfaces**: `IngestionDocumentReader`,
  `IngestionChunker<T>`, `IngestionChunkProcessor<T>`, `IngestionChunkWriter<T>`
  (`WriteAsync(IAsyncEnumerable<IngestionChunk<T>>, CancellationToken)` + `Dispose(bool)`).

**Memory profile**: O(one materialized document). `ReadAsync` returns `Task<IngestionDocument>` —
the whole document tree is in memory before chunking; chunker → writer is a genuine streaming pipe.
Meets the O(largest item) bar, where the item is a document, not a chunk. No sub-document streaming
read exists.

**Markdown**: `MarkdownReader` (`.Markdig` package, `Markdig.Signed >= 0.43.0`) — pure managed,
no external process, the reader used in every official sample. Header structure preserved (feeds
`HeaderChunker` directly). UNVERIFIED: whether tables/code fences survive as distinct structured
elements vs flattened text.

**PDF**: **no native/local PDF reader exists.** Options today are `MarkItDownReader` (shells out
to a locally installed Python MarkItDown executable) and `MarkItDownMcpReader` (talks to a
MarkItDown MCP server, Docker or pip). Both are out-of-process Python dependencies. Azure Document
Intelligence and LlamaParse readers are announced, not shipped. Markdown-only MVP is the correct
cut; PDF is a deliberate operational-dependency decision later.

**AOT/trim posture**: not certified. Neither csproj sets `IsAotCompatible`/`IsTrimmable`;
dotnet/extensions carries an open repo-wide trim-safety tracking issue (#4002) and suppresses IL
warnings monorepo-wide. Fits Kontext anyway: the MCP edge already requires the STJ reflection
fallback, so the host is reflection-enabled. Do not put DataIngestion on any AOT-published path.

**Risks**: preview churn is real (an adjacent Microsoft.Extensions.AI surface took a
binary-breaking rename in a recent cycle) — pin exact versions. Open issue #7359: test hang in
`IncrementalIngestion` with many records. Download-count anomaly on 10.4.0 (~195k vs 2–7k for
siblings) — unexplained, operationally irrelevant.

Sources: nuget.org package pages, https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion,
https://learn.microsoft.com/dotnet/ai/quickstarts/process-data,
https://devblogs.microsoft.com/dotnet/introducing-data-ingestion-building-blocks-preview/.

### 2. The repo — what the pattern to mirror actually is

Mapped in-session (two parallel Kontext systems exist on disk; the NEW `src/Kontext/Kurrent.Kontext`
is the target):

- **Core**: `IKontextMemory` (`IKontextMemory.cs:11-21`) speaks canonical
  `Kurrent.Kontext.Contracts.*` protobuf types directly — Path B, contracts ARE the core.
- **gRPC edge**: `Edges/Grpc/GrpcMemoryService.cs` — pure pass-through, no mapping.
- **MCP edge**: `Edges/Mcp/McpMemoryService.cs` + `McpMappers` — the ONE place mapping survives;
  HTTP-friendly model, string ids, streaming materialized to lists (MCP tools cannot return
  `IAsyncEnumerable`, schemas need STJ reflection fallback, descriptions in `McpInstructions.resx`).
- **Registration**: shared `AddKontext()` + `AddGrpcEdge()`/`AddMcpEdge()`
  (`ServiceCollectionExtensions.cs:74-87`), idempotent, both edges co-registrable.
- **Write path**: services append events to the log; `KontextMemoryWriter` (Surge
  consumer, `Modules/Memory/Data/`) batch-embeds and folds them into the lance read model via one
  partitioned MERGE. The reactor is the SOLE read-model writer (append-only ruling, 2026-07-20).
- **Precedent**: the deleted bulk-import endpoint (preserved verbatim, commented, in
  `KontextPrototypePlugin.cs` ~114-174; original at HEAD
  `src/KurrentDB.Plugins.Kontext/EndpointRouteBuilderExtensions.cs`) is EVENT import — MCP-session
  gated HTTP POST, `ImportEvent[]` → `ImportValidator` → `ISystemClient.WriteBatchAsync`. Adjacent
  precedent for "external payload → validated append", not document ingestion.
- Plugin wiring is mid-restructure: `KontextPlugin` currently wires the OLD `KurrentDB.Kontext`;
  `KontextPrototypePlugin` (new, fully commented out) is the migration target. Hosting for any new
  ingestion service is therefore an open question by construction.
- Stray file flagged during the sweep: `Modules/Memory/copy_KontextMemoryProjectorService.cs` —
  possible leftover duplicate, untouched.

### 3. Transport — what each edge can physically carry

**gRPC** (grpc-dotnet, .NET 10):

- Defaults: `MaxReceiveMessageSize` 4 MB both sides; `MaxSendMessageSize` unlimited. Kestrel
  HTTP/2 initial stream window 768 KB. `byte[]` > 85 KB lands on the LOH; official guidance says
  avoid large single messages, consider client streaming or a plain HTTP endpoint for large blobs.
- Canonical upload shape: client streaming, first message metadata, then 16–64 KB byte chunks
  (clears both the LOH line and the flow-control window):

```protobuf
service IngestionService {
  rpc UploadDocument (stream UploadChunk) returns (UploadResult);
}
message UploadChunk {
  oneof payload {
    DocumentMetadata metadata = 1; // first message only
    bytes data = 2;
  }
}
```

- Server reads `IAsyncStreamReader<T>.ReadAllAsync()`; progress later = flip the response to a
  server stream (bidi). No official numeric unary-vs-streaming cutoff exists (UNVERIFIED
  inference): unary `bytes` is defensible for tens-of-KB payloads only.

**MCP** (spec revision 2026-07-28, current; C# SDK `ModelContextProtocol` 2.1.0):

- **There is no client→server file-transfer primitive in the spec.** Resources are strictly
  server→client (`resources/list|read|subscribe`; no write). SEP-2356 (file inputs as data URIs)
  was closed 2026-06-26, superseded by SEP-2631 (`files/authorizeUpload`/`authorizeDownload`,
  presigned-URL-style out-of-band data plane) — NOT landed in 2026-07-28. Do not build against it.
- Practical inbound paths today: (a) content inline in a tool argument — for markdown this is
  plain text, no base64 needed; (b) a URI/path/handle argument the server resolves itself,
  bypassing the LLM channel. Community consensus: never push large payloads through the model —
  base64 adds ~33% before tokenization and multi-MB payloads run to hundreds of thousands of
  tokens (exact token ratio UNVERIFIED, tokenizer-dependent).
- No streaming in either direction for tool calls; `notifications/progress` (client-supplied
  `progressToken`) is a sideband status channel, optional support.

### 4. Fit assessment

The library slots into Kontext's architecture with one deliberate inversion: **the built-in
`VectorStoreWriter<T>` writes a store directly, but Kontext's ruling is append-only — the log is
truth and the reactor is the sole read-model writer.** The pipeline's seam for that is exactly its
extension point: a custom `IngestionChunkWriter<T>` that APPENDS chunk events via `ISystemClient`
instead of writing lance. Everything upstream (reader, chunkers, enrichers, tokenizer budgeting)
is used as-is; embedding then happens where it already happens for memories — in the
projector/writer, at fold time, giving replay-re-embeds and the embedding-model migration path for
free. The Records Indexer feature (2026-08-10, settling) is the natural downstream consumer shape.

The two edges reconcile in the core, not in each other: the core operation accepts transport-neutral
metadata + a byte/text stream (`IAsyncEnumerable<ReadOnlyMemory<byte>>` or `Stream`). The gRPC edge
maps chunk frames 1:1 (near-zero adapter). The MCP edge synthesizes the same abstraction from an
inline string or a resolved URI — it is already the one edge that pays a mapping cost, and its
constraints (no streaming returns, reflection schemas, resx descriptions) all carry over unchanged.

### 5. How shipping MCP servers ingest documents today (survey, 2026-08-11)

15 real servers surveyed, tool schemas verified from source repos/official docs. Patterns:
(a) inline content in tool args · (b) local file path · (c) URL the server fetches ·
(d) out-of-band presigned upload + handle · (e) provider-side storage id, ingestion outside MCP.

| Server | Ingestion tool | Pattern | Transport |
|---|---|---|---|
| Qdrant | `qdrant-store {information}` | (a) | stdio + streamable HTTP |
| Chroma | `chroma_add_documents {documents[]}` | (a) | stdio only |
| Pinecone Developer | `upsert-records` | (a) | stdio only |
| Pinecone Assistant | none — upload via separate API | (e) | stdio / hosted HTTP |
| Ragie | none — `retrieve` only | (e) | stdio only |
| Graphlit | `ingestUrl` / `ingestText` / `ingestFile{path}` | (c)/(a)/(b) | stdio only |
| Needle | `needle_add_file {url}` | (c) | stdio only |
| Morphik | `ingest-text`, `ingest-file-from-path`, `ingest-file-from-base64` ("the workaround for HTTP transport") | (a)/(b)/(a-b64) | stdio + streamable HTTP |
| markitdown-mcp | `convert_to_markdown(uri)` — http/file/data | (c)/(b)/(a) | stdio + HTTP (no auth) |
| Firecrawl hosted | `firecrawl_parse` two-phase: `filePath` → upload instructions → `uploadRef` | **(d)** | stdio + hosted HTTP |
| Box official hosted | `upload_file {file_content}` + `get_upload_url` (presigned, single-use) | (a) + **(d)** | remote, bearer |
| Google Drive official | `create_file {textContent?/base64Content?}` | (a) | streamable HTTP + OAuth |
| Basic Memory | `write_note {content}` | (a) | stdio primary |
| FutureSearch | `request_upload_url` → HMAC-signed URL (5-min TTL) → PUT → `artifact_id` consumed by all tools | **(d)** | remote |
| Scenario | `upload_asset`: presigned S3 multipart → `asset_id`; inline base64 ≤ 100 KB fallback | **(d)** + (a) | remote, OAuth |

**Convergence** — every server that ships real remote-HTTP document ingestion abandons local
paths and lands on two complementary patterns chosen by size: **(c) URL-fetch** when the content
already lives somewhere fetchable, and **(d) presigned out-of-band upload + handle** for arbitrary
local content — independently reinvented by Firecrawl (`uploadRef`), FutureSearch (`artifact_id`),
and Scenario (`asset_id`) before any spec primitive existed. Inline (a) survives only as a
small-file fallback, self-capped by implementers at roughly tens-to-~100 KB. **No surveyed server
streams bytes through the MCP tool-call channel.** Auth insight from the (d) implementations:
none invented a second session/token scheme for the upload route — the presigned URL is its own
authorization (HMAC/S3 signature + TTL); MCP-layer auth only gates who may MINT one. SEP-2631
(`files/authorizeUpload`) standardizes exactly this shape, so hand-building it now is
forward-compatible. Kontext prior art: the deleted bulk-import endpoint was the same sibling-route
idea but authenticated via `Mcp-Session-Id` header + `ActiveMcpSessions` lookup — the surveyed
servers' signed-URL approach avoids coupling the upload route to session state.

## Implications

Proposals, not decisions — nothing below is settled until ruled on in discussion.

1. **Proto-first core, memory-pattern clone**: new `IngestionService` in
   `Kurrent.Kontext.Contracts` (client-streaming `UploadDocument`), core port (e.g.
   `IKontextIngestion`) speaking contract types, `GrpcIngestionService` pass-through,
   `McpIngestionService` + mapper, registered via the existing `AddKontext()`/`AddGrpcEdge()`/
   `AddMcpEdge()` composition.
2. **Custom `IngestionChunkWriter<T>` → log append**, not `VectorStoreWriter` → store. Preserves
   the append-only ruling; projector embeds and folds. Alternative (direct store write via a
   MEVD-shaped writer) exists but contradicts the standing architecture — flagged, not proposed.
3. **MCP tool shape for MVP**: `ingest_document` with inline markdown string (natural — markdown
   is text; matches the survey's (a)-for-small consensus, cap ~100 KB) plus a URL variant (c).
   For PDF and large payloads, follow the surveyed convergence: a `request_upload` tool that mints
   a short-TTL signed URL on the sibling HTTP host, client PUTs bytes out-of-band, then
   `ingest_document {upload_handle}` — the Firecrawl/FutureSearch/Scenario shape, and what
   SEP-2631 will standardize. Signed-URL auth beats reviving the `Mcp-Session-Id` header gate.
4. **MVP chunker**: `HeaderChunker` (structure-preserving, no extra model calls);
   `SemanticSimilarityChunker` deferred — it spends embedding calls at ingest time. Enrichers
   deferred — Kontext has no `IChatClient` wired (Reflect still throws NotImplementedException).
5. **PDF decision deferred and explicit**: adopting `.MarkItDown` means a Python/Docker
   out-of-process dependency; the alternative is waiting for the announced Azure Document
   Intelligence / LlamaParse readers (cloud, paid). Neither is needed for the markdown MVP.
6. **Open questions for ruling**: stream naming (`$kontext/documents`?), event contract shapes
   (DocumentIngested / chunk events — one per chunk vs batched), where ingestion hosting lands
   given the plugin restructure in flight, dedup/re-ingest semantics (the library's
   `IncrementalIngestion` per-document-replace is a useful semantic to mirror on the log),
   and version pinning policy for a preview-track dependency.
