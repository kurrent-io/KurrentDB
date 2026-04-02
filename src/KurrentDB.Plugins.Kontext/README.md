# KurrentDB.Plugins.Kontext

Agent memory plugin for KurrentDB. Gives AI agents durable, searchable memory backed by KurrentDB events, with an embedded MCP (Model Context Protocol) server for tool access.

## What it does

- Subscribes to `$all` and indexes every event into a local SQLite database using hybrid text (BM25) + vector (embedding) search
- Exposes 6 MCP tools over HTTP for AI agents: `create_session`, `query_events`, `view_events`, `delete_session`, `retain_facts`, `recall_facts`
- Runs entirely in-process — no external gRPC calls, no separate processes
- Enforces KurrentDB's stream ACLs on search results (unauthorized events are redacted)

## Configuration

```yaml
# kurrentdb.conf
KurrentDB:
  Kontext:
    Enabled: true
```

Or via environment variable:

```bash
KurrentDB__Kontext__Enabled=true
```

Or command line:

```bash
dotnet KurrentDB.dll --KurrentDB:Kontext:Enabled=true
```

## Data storage

All Kontext data is stored under `{index}/kontext/`:

| File | Purpose |
|------|---------|
| `kontext.db` | SQLite database with FTS5 + vector indexes |
| `.checkpoint` | Subscription position for resuming after restart |

The `$kontext-memory` system stream stores retained facts as events.

## Connecting Claude Code

Add a `.mcp.json` file to your project root:

```json
{
  "mcpServers": {
    "kontext": {
      "type": "http",
      "url": "https://localhost:2113/mcp/kontext"
    }
  }
}
```

If authentication is required, add the `headers` field (example uses `admin:changeit` base64-encoded):

```json
{
  "mcpServers": {
    "kontext": {
      "type": "http",
      "url": "https://localhost:2113/mcp/kontext",
      "headers": {
        "Authorization": "Basic YWRtaW46Y2hhbmdlaXQ="
      }
    }
  }
}
```

For insecure mode (`--dev --insecure`), use `http://` instead of `https://`.

## MCP tools

| Tool | Description |
|------|-------------|
| `create_session` | Create a bounded search session for a specific question |
| `query_events` | Search, read, and manage events within a session |
| `view_events` | View all accumulated events in a session |
| `delete_session` | Free session resources when done |
| `retain_facts` | Store synthesized knowledge in agent memory |
| `recall_facts` | Search previously retained facts |

## Authorization

The plugin leverages KurrentDB's built-in authorization:

- The MCP endpoint is protected by KurrentDB's authentication middleware (Basic Auth, Bearer, certificates)
- Search results are post-filtered against stream ACLs — if a user doesn't have read access to a stream, the event appears in results but with `Data` and `Metadata` redacted and `AccessDenied: true`
- The `$kontext-memory` stream is a system stream — agents read/write through the plugin, which operates as the system account internally

## Architecture

```
Agent (Claude Code, etc.)
  |
  | MCP Streamable HTTP (JSON-RPC over POST)
  v
KurrentDB HTTP Pipeline (auth middleware)
  |
  v
KontextPlugin
  ├── InternalKontextClient     → Enumerator.AllSubscription + ISystemClient
  ├── SqliteSearchService       → FTS5 + sqlite-vec (BM25 + vector)
  ├── EmbeddingService          → all-MiniLM-L6-v2 (ONNX, 384-dim)
  ├── CrossEncoderService       → ms-marco-TinyBERT-L2-v2 (ONNX)
  └── KurrentDbStreamAccessChecker → IAuthorizationProvider
```
