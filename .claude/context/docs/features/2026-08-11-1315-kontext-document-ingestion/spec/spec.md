---
title: Kontext Document Ingestion — Tech Spec
status: draft            # draft | review | accepted | superseded
authors: [sergio]
date: 2026-08-11
tags: [kontext, ingestion, dataingestion, mcp, grpc, signed-url, markdown]
---

# Tech Spec — Kontext Document Ingestion

> Build target for a fresh session. Read first: the feature's [design record](../design/design.md) and the [research
> doc](../../../research/2026-08-11-1301-kontext-document-ingestion/research.md). Mirror the Kontext memory service
> throughout (`src/Kontext/Kurrent.Kontext/` — `IKontextMemory`, `Edges/Grpc/GrpcMemoryService.cs`,
> `Edges/Mcp/McpMemoryService.cs`, `ServiceCollectionExtensions.cs`).

## Overview

One core ingestion service lives in `Kurrent.Kontext`. Two edges expose it. A document enters as bytes through one of
three paths: a gRPC client stream, an out-of-band signed-URL upload that a handle references, or a small inline markdown
string on the MCP tool.

The core runs the document through the `Microsoft.Extensions.DataIngestion` pipeline (MarkdownReader → HeaderChunker)
and APPENDS chunk events to the KurrentDB log.

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

Ingestion ends at the appended events. The Records Indexer feature owns the projection of those events into the lance
read model.

The signed-URL upload is the ruled mechanism (design record, 2026-08-11). Bytes never ride the MCP JSON-RPC channel.

## Design

### Contracts (proto — the core model)

Add a new service in `src/Kontext/Kurrent.Kontext.Contracts` beside the memory protos. The contracts ARE the core (Path
B). The core port speaks these types. There is no separate domain model.

```protobuf
service IngestionService {
  // gRPC-native upload: first message metadata, then 16-64 KB byte chunks.
  rpc IngestDocument (stream IngestDocumentRequest) returns (IngestDocumentResponse);
  // Mints a short-TTL signed upload URL. MCP-edge primary path; also callable over gRPC.
  rpc RequestUpload (RequestUploadRequest) returns (RequestUploadResponse);
  // Ingests a previously completed signed-URL upload by handle.
  rpc IngestUpload (IngestUploadRequest) returns (IngestDocumentResponse);
}

message IngestDocumentRequest {
  oneof payload {
    DocumentMetadata metadata = 1;   // first message only
    bytes chunk = 2;
  }
}
message DocumentMetadata {
  string document_id = 1;            // optional; server generates when empty
  string name = 2;
  string media_type = 3;             // "text/markdown" for MVP
  repeated string tags = 4;
  string source = 5;                 // optional provenance (uri, tool, agent)
}
message RequestUploadRequest  { string name = 1; string media_type = 2; uint64 size_hint = 3; }
message RequestUploadResponse { string upload_id = 1; string upload_url = 2;
                                int64 expires_at_unix_ms = 3; uint64 max_bytes = 4; }
message IngestUploadRequest   { string upload_id = 1; DocumentMetadata metadata = 2; }
message IngestDocumentResponse { string document_id = 1; uint32 chunk_count = 2;
                                 uint64 log_position = 3; }
```

### Core (`src/Kontext/Kurrent.Kontext/Modules/Ingestion/`)

`IKontextIngestion` is the port. It takes and returns contract types, like `IKontextMemory`:

- `IngestAsync(DocumentMetadata, IAsyncEnumerable<ReadOnlyMemory<byte>>, CancellationToken)`
- `RequestUploadAsync(RequestUploadRequest, CancellationToken)`
- `IngestUploadAsync(IngestUploadRequest, CancellationToken)`

`KontextIngestion` implements the port. Both ingest entry points follow the same flow. First, resolve the byte source to
a `Stream`. Then run the pipeline:

1. `MarkdownReader.ReadAsync(stream, identifier, mediaType)` materializes ONE document. That is the accepted memory
   profile.
2. `HeaderChunker.ProcessAsync(document)` streams `IngestionChunk<string>` values.
3. `KontextChunkAppendWriter.WriteAsync(chunks)` batches the appends.

Do NOT use `IngestionPipeline<T>`. It is directory-oriented. Compose the reader, the chunker, and the writer directly
against the stream.

`KontextChunkAppendWriter : IngestionChunkWriter<string>` is the custom sink. It buffers chunks to a batch budget
(default: 50 chunks or 1 MB of content, whichever comes first). It appends each batch through the system client to the
document's stream. It writes nothing to lance/DuckDB. Dispose flushes the last batch.

Chunking defaults are spec defaults, not rulings (design record, Open Question 4). Surface them as options:
`IngestionChunkerOptions(TiktokenTokenizer.CreateForEncoding("o200k_base"))` with `MaxTokensPerChunk = 1024` and
`OverlapTokens = 0`. The tokenizer only measures the budget. It makes no model calls.

The MVP accepts the `text/markdown` media type only (with bare `.md` name inference). Reject all other types with
`InvalidArgument` or a typed exception. PDF arrives later behind the same surface.

Validation uses FluentValidation validators and the same decorator pattern as the memory service
(`Infrastructure/FluentValidation/`) over the canonical request types.

### Signed-URL upload subsystem (`Modules/Ingestion/Uploads/`)

`UploadStaging` (one class, DI singleton) owns the full lifecycle: mint → receive → consume → expire.

```text
╭──────◯─────╮
│     ●      │
╰──────◯─────╯
       │
   mint│
       ▼
╭────────────╮
│  Pending   │
╰──────┬─────╯
       │
       ├───────────────────────────╮TTL / janitor
 PUT ok│                           │
       ▼                           ▼
╭────────────╮              ╭────────────╮
│            │TTL / janitor │            │
│  Received  ├─────────────►│  Expired   │
│            │              │            │
╰──────┬─────╯              ╰────────────╯
       │
       │IngestUpload
       ▼
╭────────────╮
│  Consumed  │
╰──────┬─────╯
       │
       │file deleted
       ▼
╭──────◯─────╮
│     ◉      │
╰──────◯─────╯
```

**Mint** (`RequestUpload`). Generate `upload_id` (guid, N format). Compute `expires = now + ttl`. Sign `sig =
HMAC-SHA256(key, "{upload_id}\n{expires_unix_ms}")` and encode it base64url. The URL shape is `PUT
{publicBase}/kontext/uploads/{upload_id}?expires={unix_ms}&sig={sig}`. Register the pending entry (id, expiry, max
bytes, state = Pending) in an in-process registry.

**Signing key.** Generate 32 random bytes per process at startup. The URL is its own authorization (the survey
convergence). The auth that the edge already carries gates the minting. A per-process key gives single-node semantics. A
configured shared key is the documented cluster extension. Do not build it now.

**Receive** (HTTP route).

1. Validate the signature with `CryptographicOperations.FixedTimeEquals`.
2. Reject an expired URL with 410. Reject a bad signature with 401. Reject an already consumed or receiving entry with
   409.
3. Reject a body over `max_bytes` with 413. Enforce the cap BOTH from `Content-Length` when present and with a counting
   stream during the copy.
4. Stream the body straight to the staging file. Never buffer the payload in memory.
5. Set the state to Received.

**Consume** (`IngestUpload`). Open the staged file as the ingest stream. Set the state to Consumed. Delete the file
after ingestion completes, on success or on failure. The events either landed, or the caller retries with a fresh
upload.

**Janitor.** A timer sweep deletes expired Pending and Received entries and their files. The interval equals the TTL.

**Staging location.** `{node data dir}/kontext/uploads/{upload_id}.tmp` (design record, Open Question 3).

The full signed-URL loop across both channels:

```text
 ┌─────────┐            ┌────────────┐         ┌───────────┐              ┌────────────┐    ┌────────┐
 │  Agent  │            │  MCP edge  │         │  Staging  │              │  HTTP PUT  │    │  Core  │
 └─────────┘            └────────────┘         └───────────┘              └────────────┘    └────────┘
      ┆ request_upload         ┆                     ┆                           ┆               ┆
      ─────────────────────────►                     ┆                           ┆               ┆
      ┆                        ┆ mint (TTL 5 min)    ┆                           ┆               ┆
      ┆                        ──────────────────────►                           ┆               ┆
      ┆ upload_url + id        ┆                     ┆                           ┆               ┆
      ◄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄                           ┆               ┆
      ┆ PUT bytes              ┆                     ┆                           ┆               ┆
      ───────────────────────────────────────────────────────────────────────────►               ┆
      ┆                        ┆                     ┆ verify sig, stage file    ┆               ┆
      ┆                        ┆                     ◄────────────────────────────               ┆
      ┆ ingest_document(id)    ┆                     ┆                           ┆               ┆
      ─────────────────────────►                     ┆                           ┆               ┆
      ┆                        ┆ IngestUpload        ┆                           ┆               ┆
      ┆                        ──────────────────────────────────────────────────────────────────►
      ┆                        ┆                     ┆ open staged file          ┆               ┆
      ┆                        ┆                     ◄────────────────────────────────────────────
      ┆                        ┆                     ┆                           ┆               ┆ chunk, append
      ┆                        ┆                     ┆                           ┆               ┆───────────────┐
      ┆                        ┆                     ┆                           ┆               ◄───────────────┘
      ┆ document_id, count     ┆                     ┆                           ┆               ┆
      ◄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
```

`KontextIngestionOptions` is a mutable class per the repo convention, never a record. Members: `UploadTtl` (default 5
min), `MaxUploadBytes` (default 64 MB), `MaxInlineContentBytes` (default 100 KB), `StagingPath`, `PublicBaseUrl`,
`MaxTokensPerChunk`, `OverlapTokens`, `AppendBatchSize`.

### gRPC edge (`Edges/Grpc/GrpcIngestionService.cs`)

Implement `Contracts.IngestionService.IngestionServiceBase` as a pure pass-through with zero mapping, exactly like
`GrpcMemoryService`. `IngestDocument` reads `IAsyncStreamReader<IngestDocumentRequest>`. The first message MUST be
`metadata`. Otherwise return `InvalidArgument`. The subsequent `chunk` frames adapt directly to the core's
`IAsyncEnumerable<ReadOnlyMemory<byte>>`. Set `MaxReceiveMessageSize` explicitly on the gRPC server options. The chunks
are small, so the cap only needs to clear one frame, not one file.

### MCP edge (`Edges/Mcp/McpIngestionService.cs` + mapper)

The edge has two tools and an HTTP-friendly model that maps to the contracts. All the existing MCP edge constraints
apply: mutable sealed classes, string ids, no `IAsyncEnumerable` returns, the STJ reflection fallback, and descriptions
in `McpInstructions.resx` through `WithToolsFromResources<T>()`.

`request_upload { name, media_type, size_hint? }` returns `{ upload_id, upload_url, expires_at, max_bytes }`. The
instructions text tells the agent: PUT the raw file bytes to `upload_url` with plain HTTP before it expires, then call
`ingest_document` with `upload_id`. The model never base64s the bytes.

`ingest_document { upload_id?, content?, name, tags?, source? }` takes `upload_id` XOR `content`. Validate that rule:
both or neither is an error. `content` is inline markdown TEXT for small payloads only. Reject content over
`MaxInlineContentBytes` (UTF-8 byte count) with a message that directs the caller to `request_upload`. The tool returns
`{ document_id, chunk_count }`.

### HTTP upload route

`MapKontextUploads(this IEndpointRouteBuilder, string pattern)` is a minimal-API PUT endpoint that delegates entirely to
`UploadStaging`. It does no MCP session lookup and requires no bearer token. The signature IS the authorization (ruled —
see the design record). The deleted bulk-import endpoint's `Mcp-Session-Id` gate is explicitly NOT revived.

Wire the route beside the MCP edge mapping, in whichever plugin shape is live at build time (design record, Open
Question 2). `KontextPlugin` is live today, and the `KontextPrototypePlugin` restructure is in flight. Check the working
tree before wiring. Ask if the placement is ambiguous.

### Events and streams

These are spec defaults, not rulings (design record, Open Question 1). Confirm with Sérgio before building if anything
feels off.

- One stream per document: `$kontext/documents/{document_id}`.
- `DocumentIngested` carries: document_id, name, media_type, tags, source, chunk_count, the content SHA-256, and
  ingested_at (unix ms — the BIGINT epoch-ms ruling applies to any later read model).
- `DocumentChunkIngested` carries: document_id, chunk_index, content, context (the chunker's header context), and
  token_count.
- Append the chunk batches BEFORE the summary `DocumentIngested` closes the ingestion. Readers treat `DocumentIngested`
  as the completion marker.
- A re-ingest with the same `document_id` appends a fresh generation (a new `DocumentIngested`). Replace-versus-version
  semantics on the read model belong to the downstream indexer. Dedup is out of scope here.

### Packages

Add to `src/Directory.Packages.props` and pin the EXACT preview versions — preview-track churn is real:

- `Microsoft.Extensions.DataIngestion`, `.Abstractions`, `.Markdig` @ `10.8.0-preview.1.26364.2`
- `Microsoft.ML.Tokenizers` (plus its o200k data package if that is split out)

Reference them from `Kurrent.Kontext.csproj`. Check that `nuget.config` packageSourceMapping covers them (they resolve
through the `*` → nuget.org mapping). Do NOT add `.MarkItDown`. PDF is out of MVP scope.

## Alternatives Considered

Verdicts only. The design record and the research doc hold the full discussion.

- `VectorStoreWriter<T>` direct-to-store — lost. It violates the append-only ruling: the log is truth, and the reactor
  is the sole read-model writer.
- Base64 document bytes through the MCP tool channel — lost. Token inflation. No surveyed remote server does it beyond a
  ~100 KB fallback.
- An `Mcp-Session-Id`-gated upload route (the deleted bulk-import shape) — lost. It couples the upload route to session
  state. The signed URL is self-authorizing (survey: FutureSearch, Scenario, Firecrawl, Box).
- Waiting for SEP-2631 — lost. It has not landed and no client supports it. The hand-built shape is identical and
  forward-compatible.
- `IngestionPipeline<T>` orchestration — lost. It is directory-oriented. Stream-oriented composition of the reader, the
  chunker, and the writer is the fit.

## Edge Cases & Failure Modes

- A gRPC stream whose first message is not `metadata`, or that has zero chunk frames → `InvalidArgument`.
- An empty document, or markdown that yields zero chunks → succeed with `chunk_count = 0` and the `DocumentIngested`
  marker only.
- Upload legs: an expired URL → 410. A tampered signature → 401 (constant-time compare). A second PUT to the same id →
  409. A body over the cap → 413, and delete the partial file. A PUT aborted mid-stream leaves the entry Pending, and
  the janitor reclaims it.
- `IngestUpload` for an id that is Pending (never PUT), expired, or already Consumed → typed not-found or conflict
  errors. Map them to `FailedPrecondition`/`NotFound` on gRPC and to clear tool errors on MCP.
- Inline `content` over the cap → reject with the `request_upload` redirect message.
- A non-UTF-8 payload or a non-markdown media type → `InvalidArgument`. The MVP is markdown-only.
- Cancellation mid-ingestion: appended batches stay appended. There is no compensation. No `DocumentIngested` marker is
  written, so readers treat the document as incomplete. Re-ingest is the recovery.
- Concurrency: the state machine permits one ingestion per upload_id. Concurrent ingestion of different documents is
  unrestricted.

## Testing

The repo conventions apply: TUnit, `Assert.That`, snake_case names, `ValueTask` tests, the Bogus faker, AAA comments,
`ITestDataSource`-style data, and the test-runner script. See `.claude/docs/testing.md` and the testing section of the
session instructions. Tests live in `Kurrent.Kontext.Tests` under `Unit/` and `Integration/`.

Unit surface:

- Signing: a mint → validate round-trip. A tampered id, expiry, or sig is rejected. The expiry boundary. The
  constant-time path is exercised.
- The `UploadStaging` state machine: pending → received → consumed. A double PUT. A consume-before-PUT. The janitor
  sweep removes expired files.
- `KontextChunkAppendWriter`: batch boundaries (count and byte budget), flush-on-dispose, ordering (chunk_index is
  monotonic), and the completion marker after all batches.
- The pipeline: representative markdown (headers, code fences, tables) → the expected chunk contexts and counts. A
  zero-chunk document. An oversize inline rejection.
- The MCP model mapping and the XOR validation (`upload_id` versus `content`).

Integration surface:

- gRPC `IngestDocument`: stream a markdown file in 32 KB frames → events on `$kontext/documents/{id}` with the correct
  counts and marker.
- The full signed-URL loop: `RequestUpload` → an HTTP PUT against the real Kestrel route → `IngestUpload` → the events
  land. Failure legs: expired, tampered, oversized.
- The MCP tools end-to-end through the in-memory MCP client/server smoke pattern used for the memory edge. The host runs
  reflection-enabled. Set `#:property PublishAot=false` if a file-based probe is used.

## Rollout

- This is a new surface only. No existing API changes and no migration. The new proto service is additive to
  `Kurrent.Kontext.Contracts`. Sérgio already directed this public-surface addition. Anything beyond the sketch above
  needs his sign-off.
- Registration composes into the existing pattern: `AddKontextIngestion()` alongside `AddKontext()`, exposed through the
  same `AddGrpcEdge()`/`AddMcpEdge()` composition, plus `MapKontextUploads()` at the host. Hosting placement follows the
  state of the plugin restructure at build time.
- Sequencing: contracts → core + staging → gRPC edge → HTTP route → MCP edge → integration tests. The downstream
  projector (the Records Indexer feature) consumes the events later. Nothing here blocks on it.
- Rollback: remove the registration. The appended `$kontext/documents/*` events are inert without a consumer.
