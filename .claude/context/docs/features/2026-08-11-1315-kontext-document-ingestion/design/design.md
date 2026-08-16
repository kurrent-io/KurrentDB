---
title: Kontext Document Ingestion
status: settling         # exploring | settling | superseded
authors: [sergio]
date: 2026-08-11
tags: [kontext, ingestion, dataingestion, mcp, grpc, signed-url, markdown, pdf]
---

# Design Space — Kontext Document Ingestion

## Problem / Trigger

Kontext must accept an uploaded document, process it, and store the result. Markdown comes first. PDF comes after. The
feature has one core implementation and two edges (gRPC + MCP), which mirrors the Kontext memory pattern. The processing
pipeline is `Microsoft.Extensions.DataIngestion`. Sérgio gave the directive on 2026-08-11.

The [research doc](../../../research/2026-08-11-1301-kontext-document-ingestion/research.md) holds the sources and the
verification detail (library verification, repo topology, transport constraints, MCP server survey). This design space
distills it.

## Exploration

### The library

`Microsoft.Extensions.DataIngestion` 10.8.0-preview.1 lives in `dotnet/extensions` (MIT, preview since 2025-11). The
pipeline stages are: `IngestionDocumentReader` → `IngestionDocument` (one document materialized) → `IngestionChunker<T>`
(streams chunks) → `IngestionChunkWriter<T>` (streaming sink, abstract class). `MarkdownReader` (Markdig, pure managed)
reads markdown.

PDF has NO native reader. The only options are MarkItDown wrappers, which need an out-of-process Python or Docker
dependency. The library is not AOT/trim-certified. That is acceptable because the MCP edge already requires the
reflection-enabled host. The library's version wave matches the repo pins (ME.AI 10.8.3, MEVD 10.8.0).

### The pattern to mirror

The `IKontextMemory` core speaks the canonical `Kurrent.Kontext.Contracts.*` types directly (Path B — the contracts ARE
the core). The gRPC edge is a pure pass-through. The MCP edge is the one place where mapping survives: HTTP-friendly
mutable classes, string ids, resx descriptions, no `IAsyncEnumerable` returns, STJ reflection schemas. Registration
composes `AddKontext()` with `AddGrpcEdge()` and `AddMcpEdge()`.

### The write path

The standing architecture is append-only. The log is truth, and the reactor is the sole read-model writer (rulings,
2026-07-20). Ingestion therefore APPENDS events. It never writes lance/DuckDB directly.

A custom `IngestionChunkWriter<T>` that appends replaces the library's built-in `VectorStoreWriter<T>`. Embedding stays
in the downstream projector, so replay re-embeds and the embedding-model migration path stays free. Downstream indexing
belongs to the Records Indexer feature
([2026-08-10-1619-kontext-records-indexer](../../2026-08-10-1619-kontext-records-indexer/)). This feature's scope ends
at the appended events.

### The upload transport problem

gRPC carries bytes natively through client streaming: a metadata-first message, then 16–64 KB chunks. The relevant
limits are the 85 KB LOH line, the 768 KB HTTP/2 flow window, and the 4 MB default receive cap.

MCP does NOT carry bytes. The 2026-07-28 spec has no client→server file-transfer primitive. The maintainers closed
SEP-2356. SEP-2631 (`files/authorizeUpload`) has not landed.

A survey of 15 shipping MCP servers shows that every well-engineered remote server keeps bytes off the MCP channel.
Inline content appears only as a small-file fallback, self-capped near 100 KB. URL-fetch serves content that is already
hosted.

For real uploads, the servers converge on one shape: a tool mints a presigned URL, the client PUTs the bytes
out-of-band, and the next tool call consumes the returned handle (Firecrawl `uploadRef`, FutureSearch `artifact_id`,
Scenario `asset_id`, Box `get_upload_url`). None of them added a second session or token scheme to the upload route. The
signed URL is its own authorization. MCP-layer auth only gates the minting.

Prior art exists in the repo. The deleted bulk-import endpoint (preserved as a comment in `KontextPrototypePlugin.cs`)
used the same sibling-HTTP-route idea. It authenticated with the `Mcp-Session-Id` header and an `ActiveMcpSessions`
lookup. The signed-URL scheme avoids that session-state coupling.

### The resulting shape

Two edges fan into one core. The core ends at the log. The Records Indexer consumes the events later.

```text
┌────────────────────────────┐    ┌─────────────────────────┐
│         gRPC edge:         │    │        MCP edge:        │
│       UploadDocument       │    │     ingest_document     │
└─────────────┬──────────────┘    └────────────┬────────────┘
              │                                │
              │ ╭──────────────────────────────╯
              ▼ ▼
┌────────────────────────────┐
│     IKontextIngestion      │
└──────────────┬─────────────┘
               ▼
┌────────────────────────────┐
│       MarkdownReader       │
└──────────────┬─────────────┘
               ▼
┌────────────────────────────┐
│       HeaderChunker        │
└──────────────┬─────────────┘
               ▼
┌────────────────────────────┐
│  KontextChunkAppendWriter  │
└──────────────┬─────────────┘
               ▼
┌────────────────────────────┐
│       KurrentDB log        │
└──────────────┬─────────────┘
               ┆
               ▼
┌────────────────────────────┐
│    Records Indexer (out    │
│         of scope)          │
└────────────────────────────┘
```

## Decisions

- 2026-08-11 — **The signed URL is the upload solution** (Sérgio's ruling, in his words: "the signed URL is the
  solution"). An MCP tool mints a short-TTL HMAC-signed URL. The client PUTs the bytes to a sibling HTTP route outside
  the JSON-RPC channel. The ingestion tool consumes the returned upload handle. Rejected: base64 through the model
  (token inflation, ~33% overhead before tokenization). Rejected: a revived `Mcp-Session-Id` header gate (session-store
  coupling on the upload route). Rejected: waiting for SEP-2631 (unlanded, and the hand-built shape is identical and
  forward-compatible).
- 2026-08-11 — **Single core, two edges, contracts-first.** Clone the memory pattern: a new proto service in
  `Kurrent.Kontext.Contracts`, a core port that speaks the contract types, a gRPC pass-through, and an MCP mapping edge.
  Rejected: an ingestion-specific domain model. Sérgio consciously reversed Path A for memory. Do not reintroduce it
  here.
- 2026-08-11 — **`Microsoft.Extensions.DataIngestion` is the processing pipeline.** The reader and the chunker are used
  as-is. An event-appending writer replaces the writer seam (`IngestionChunkWriter<T>`, abstract class). Rejected:
  `VectorStoreWriter<T>` direct-to-store, which contradicts the append-only ruling. Rejected: hand-rolled markdown
  parsing — the library is the directive.
- 2026-08-11 — **Markdown-only MVP** with `MarkdownReader`/Markdig. The MVP defers PDF: no native reader exists, and
  MarkItDown means a Python/Docker operational dependency. That adoption is a deliberate subsequent decision, not an MVP
  default.
- 2026-08-11 — **Inline markdown stays as the small-payload convenience** on the MCP tool, capped. Markdown is text, and
  the surveyed servers uniformly keep a small-file fallback. The signed-URL path is the canonical upload mechanism and
  the only path for PDF and large payloads.

## Open Questions

1. Stream naming and event contract shapes. The spec proposes `$kontext/documents/{document_id}` with `DocumentIngested`
   plus batched `DocumentChunkIngested`. These are spec defaults, not Sérgio rulings. Confirm at build time.
2. Where ingestion hosting lands. The plugin restructure is mid-flight: `KontextPlugin` wires the OLD system, and
   `KontextPrototypePlugin` is fully commented out.
3. Staging store placement and retention. The spec proposes disk staging under the node data dir with a TTL janitor.
4. Chunker defaults. The spec proposes `HeaderChunker` with a token budget. `SemanticSimilarityChunker` spends embedding
   calls at ingest time, so it is deferred.
5. Enrichers (Summary, Keyword, and the rest) need an `IChatClient`. Kontext has none wired. Deferred.
6. Re-ingest and dedup semantics. The library's `IncrementalIngestion` per-document-replace is a candidate semantic to
   mirror on the log (supersede-style). Undecided.
