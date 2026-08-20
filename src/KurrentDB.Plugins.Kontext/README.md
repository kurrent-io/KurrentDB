# KurrentDB.Plugins.Kontext

Agent memory + RAG plugin for KurrentDB. Gives AI agents durable, searchable knowledge backed by KurrentDB events, with an embedded MCP (Model Context Protocol) server for tool access.

Requires a license with the `KONTEXT` entitlement.

## Minimal configuration

`Enabled` is the only required setting:

```yaml
KurrentDB:
  Kontext:
    Enabled: true
```

Or via environment variable:

```bash
KurrentDB__Kontext__Enabled=true
```

With this, storage lives under `{index}/kontext/` and embeddings use the in-process ONNX provider (paraphrase-multilingual-MiniLM-L12-v2, 384-dim, CPU-only, no API key).

## Full configuration

```yaml
KurrentDB:
  Kontext:
    Enabled: true
    Path: /var/lib/kurrentdb/kontext        # directory where kontext stores all its data (defaults to {index}/kontext)
    Embeddings:
      Provider: Local                       # Local (default) | OpenAI | Ollama | GoogleVertexAI | AmazonBedrock
      Local:                                # in-process ONNX (paraphrase-multilingual-MiniLM-L12-v2, 384-dim, CPU-only, no API key)
        BatchSize: 1
      OpenAI:                               # ApiKey is required when Provider is OpenAI
        ApiKey: sk-...
        Model: text-embedding-3-small
        Endpoint: https://api.openai.com    # optional, set for OpenAI-compatible proxies
        BatchSize: 256
      Ollama:
        Endpoint: http://localhost:11434
        Model: nomic-embed-text
        BatchSize: 16
      GoogleVertexAI:                       # ProjectId + Region required; uses Application Default Credentials
        ProjectId: my-gcp-project
        Region: us-central1
        Model: text-embedding-004
        BatchSize: 100
      AmazonBedrock:                        # Region required; uses the default AWS credential chain
        Region: us-east-1
        Model: amazon.titan-embed-text-v2:0
        BatchSize: 96
```
Only the sub-block matching the selected `Provider` is read.

### Model requirements

The embedding model must produce **384-dimensional** vectors. A model of any other width fails at the
first write: the storage engine rejects the row instead of storing a bad vector, so a misconfigured
model cannot corrupt the store.

Retrieval quality depends on the model. Only the shipped default is benchmarked — any other model
runs, but its quality is unmeasured.

Changing the model means resetting the store. Vectors written by the old model are not comparable
with vectors from the new one. A model of a **different** width fails loudly on the first write. A
model of the **same** width does not: it stores successfully and degrades retrieval quality with no
error at all. That case is the one you must catch yourself.

[//]: # (## Workspaces)

[//]: # ()
[//]: # (A workspace is an isolated scope with its own memory and event index. Workspaces are managed via the control-plane HTTP API at `/kontext/workspaces` &#40;these require KurrentDB admin credentials; an agent can obtain ready-to-run curl commands via the `ws_management` MCP tool&#41;:)

[//]: # ()
[//]: # (- `POST /kontext/workspaces` — create)

[//]: # (- `GET /kontext/workspaces` — list)

[//]: # (- `GET /kontext/workspaces/{name}` — view)

[//]: # (- `POST /kontext/workspaces/{name}/start` — start)

[//]: # (- `POST /kontext/workspaces/{name}/stop` — stop)

[//]: # (- `DELETE /kontext/workspaces/{name}` — delete)

[//]: # ()
[//]: # (Each workspace defines:)

[//]: # ()
[//]: # (- **Filter rules** — stream-prefix patterns paired with optional Jint JavaScript expressions &#40;e.g. `rec => rec.value.userId === 123`&#41; that decide which events get indexed)

[//]: # (- **Operational flags** — `DisableMemory`, `DisableImports`, `DisableInquiries`, `ReadOnly`. Tools called against a workspace that forbids the operation throw a clear error.)

[//]: # ()
[//]: # (The `default` workspace is auto-created on first startup with a match-everything rule.)

[//]: # ()
[//]: # (## MCP tools)

[//]: # ()
[//]: # (Tools are organized by name prefix.)

[//]: # ()
[//]: # (### Memory &#40;`mem_*`&#41; — durable facts across chat sessions)

[//]: # ()
[//]: # (| Tool | Description                                            |)

[//]: # (|------|--------------------------------------------------------|)

[//]: # (| `mem_recall` | Search retained facts in the active workspace          |)

[//]: # (| `mem_retain` | Save a fact under a topic &#40;reusing a topic overwrites&#41; |)

[//]: # (| `mem_topics` | Discover existing topics in the active workspace              |)

[//]: # ()
[//]: # (### Inquiries &#40;`inq_*`&#41; — search and read raw events as grounding)

[//]: # ()
[//]: # (| Tool | Description                                                        |)

[//]: # (|------|--------------------------------------------------------------------|)

[//]: # (| `inq_new` | Open an inquiry that accumulates events across inq_search / inq_read calls |)

[//]: # (| `inq_search` | Hybrid FTS + vector search; adds hits to the inquiry's working set |)

[//]: # (| `inq_read` | Read events from a stream into the working set                     |)

[//]: # (| `inq_view` | Review the current working set                                     |)

[//]: # (| `inq_forget` | Drop noise from the working set                                    |)

[//]: # (| `inq_end` | Free inquiry resources                                             |)

[//]: # ()
[//]: # (### Workspace &#40;`ws_*`&#41; — introspect indexing, import data, manage workspaces)

[//]: # ()
[//]: # (| Tool | Description                                           |)

[//]: # (|------|-------------------------------------------------------|)

[//]: # (| `ws_status` | Indexing progress &#40;caught up / catching up / stopped / disabled&#41; |)

[//]: # (| `ws_streams` | Discover streams in the active workspace       |)

[//]: # (| `ws_import` | Returns the URL and payload format for the bulk-import HTTP endpoint |)

[//]: # (| `ws_management` | Returns curl commands &#40;require admin&#41; to list, view, create, start, stop, or delete workspaces |)

[//]: # ()
[//]: # (## Bulk import)

[//]: # ()
[//]: # (External data is loaded via a two-step flow: the agent calls `ws_import` to obtain the URL and payload format, then uses any HTTP client &#40;curl, wget, Invoke-WebRequest, ...&#41; to POST a JSON array of events:)

[//]: # ()
[//]: # (```)

[//]: # (POST /kontext/{workspace}/import)

[//]: # (Content-Type: application/json)

[//]: # (Mcp-Session-Id: <session id from active MCP session>)

[//]: # ()
[//]: # ([)

[//]: # (  { "Stream": "orders-1", "EventType": "OrderPlaced", "Data": { ... } },)

[//]: # (  { "Stream": "orders-1", "EventType": "OrderShipped", "Data": { ... } })

[//]: # (])

[//]: # (```)

[//]: # ()
[//]: # (The endpoint requires an active MCP session &#40;the `Mcp-Session-Id` header&#41;. Events that don't match the workspace's stream prefixes / JS filters are rejected or written-but-not-indexed &#40;per validator rules&#41;.)

## Authorization

The plugin leverages KurrentDB's built-in authorization:

- All endpoints are protected by KurrentDB's authentication middleware (Basic Auth, Bearer, certificates)
- Search results are post-filtered through KurrentDB's stream-access authorization — if the caller can't read a stream, the event still appears in results but with `Data` / `Metadata` redacted
- Internal subscriptions, writes, and indexing operate as the system account

## Connecting an AI agent

Most MCP-aware AI agents (Claude Code, Cline, Cursor, custom clients, ...) connect via Streamable HTTP. Point the agent at the workspace endpoint — the default workspace is at `/kontext/mcp/default`, and any other workspace is at `/kontext/mcp/{workspace}`.

The exact config format depends on the agent. Claude Code, for example, uses a `.mcp.json` file in the project root. Pass credentials via the `Authorization` header (example uses `admin:changeit` base64-encoded):

```json
{
  "mcpServers": {
    "kontext": {
      "type": "http",
      "url": "https://localhost:2113/kontext/mcp/default",
      "headers": {
        "Authorization": "Basic YWRtaW46Y2hhbmdlaXQ="
      }
    }
  }
}
```

For insecure mode (`--dev --insecure`), use `http://` instead of `https://`.
