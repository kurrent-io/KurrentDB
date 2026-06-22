---
order: 8
---

# Kontext

Kontext gives AI agents durable, searchable knowledge backed by KurrentDB events. It indexes events into both a full-text search store (for keyword search) and a vector store of embeddings (for semantic search), then exposes them through an embedded [MCP](https://modelcontextprotocol.io/) (Model Context Protocol) server. Any MCP-aware agent can connect over HTTP to remember facts and ground its answers in past events. On the LoCoMo long-term memory benchmark, Kontext scores 86%.

::: warning Preview
Kontext is a preview feature and may change in future releases.
:::

A [license key](../quick-start/installation.md#license-keys) with the `KONTEXT` entitlement is required.

## Use cases

- **Enterprise**
  - **Unlocking your history** — KurrentDB keeps what other databases throw away: your complete history — every change, with its context; an audit log by construction. Kontext puts that asset to work: AI agents connect directly to KurrentDB and turn years of events into answers in minutes — analysis that would otherwise take days of sifting.
  - **Live agents** — connect agents to Kontext so they can recall durable facts and search historical events while assisting users. For example, a support system backed by KurrentDB can give its online agents searchable ticket and conversation history to resolve customer issues faster.
  - **Workflows** — a multi-step agentic workflow (e.g. ticket triage, enrichment, or monitoring) can call Kontext between steps: searching past events for grounding (RAG) and retaining what it learns as durable memory that later steps and runs build on.
- **Personal** — Kontext can serve as a personal agent memory store for an MCP-compatible assistant such as OpenClaw, letting it remember facts and recall prior context across sessions.

## Concepts

- **Workspace** — an isolated scope with its own memory and event index. Filter rules decide which events are indexed into it. A `default` workspace that indexes all events is created automatically, but is stopped by default. An agent connects to exactly one workspace.
- **Memory** — durable facts saved under a topic, retained across the agent's chat sessions.
- **Inquiry** — a working set of events gathered by searching and reading the log to ground one line of investigation.

## Configuration

### Enabling Kontext

`Enabled` is the only required setting:

```yaml
KurrentDB:
  Kontext:
    Enabled: true
```

### Storage

Kontext stores all its data under `{index}/kontext/` by default. Set `Path` to store it elsewhere:

```yaml
KurrentDB:
  Kontext:
    Enabled: true
    Path: /var/lib/kurrentdb/kontext
```

### Embeddings

By default embeddings are generated in-process with a free, local, CPU-only model (no API key required). Other providers are supported — choose one under `Embeddings`:

```yaml
KurrentDB:
  Kontext:
    Enabled: true
    Embeddings:
      Provider: OpenAI         # Local (default) | OpenAI | Ollama | GoogleVertexAI | AmazonBedrock
      OpenAI:
        ApiKey: sk-...          # required when Provider is OpenAI
        Model: text-embedding-3-small
```

Each provider's settings, with their defaults:

| Provider | Required settings | Optional settings |
|----------|-------------------|-------------------|
| `Local` | None — in-process ONNX (all-MiniLM-L6-v2, CPU-only, no API key). Default. | |
| `OpenAI` | `ApiKey` | `Model` (`text-embedding-3-small`) · `Endpoint` (unset — set for OpenAI-compatible proxies) · `BatchSize` (`256`) |
| `Ollama` | None | `Endpoint` (`http://localhost:11434`) · `Model` (`nomic-embed-text`) · `BatchSize` (`16`) |
| `GoogleVertexAI` | `ProjectId`, `Region` (uses [Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials)) | `Model` (`text-embedding-004`) · `BatchSize` (`100`) |
| `AmazonBedrock` | `Region` (uses the [default AWS credential chain](https://docs.aws.amazon.com/sdkref/latest/guide/standardized-credentials.html)) | `Model` (`amazon.titan-embed-text-v2:0` — Amazon Titan and Cohere embed models, e.g. `cohere.embed-english-v3`) · `BatchSize` (`96`) |

`BatchSize` is the number of texts embedded per request. Only the sub-block matching the selected `Provider` is read.

::: warning
Changing the embedding model invalidates the existing semantic index — it must be rebuilt.
:::

## Connecting an agent

Kontext is exposed over MCP Streamable HTTP. Point the agent at the workspace endpoint — the `default` workspace is at `/kontext/mcp/default`, and any other workspace at `/kontext/mcp/{workspace}`.

The exact config depends on the agent. For example, a `.mcp.json` file compatible with Claude Code:

```json
{
  "mcpServers": {
    "kontext": {
      "type": "http",
      "url": "https://localhost:2113/kontext/mcp/default",
      "headers": {
        "Authorization": "Basic c3VwcG9ydC1hZ2VudDpzM2NyM3Q="
      }
    }
  }
}
```

The `Authorization` header is the Base64 encoding of `<user>:<password>` (here, `support-agent:s3cr3t`).

::: warning
For quick testing you can use the admin account — `admin:changeit`, encoded as `YWRtaW46Y2hhbmdlaXQ=`. This is not recommended in production: the agent would then have full administrator access, including managing workspaces and reading every stream.
:::

Requests are authenticated and authorized by KurrentDB — when an agent reads raw events (the `inq_*` tools), it only sees data from streams the user it connected as is allowed to read. You can therefore use standard stream [ACLs](../security/user-authorization.md) — or any other stream-authorization plugin built into KurrentDB — to control which events an agent can access.

## MCP tools

Once connected to a workspace over MCP, an agent has access to the following tools, grouped by name prefix:

**Memory (`mem_*`)** — save and recall durable facts:

| Tool | What it does |
|------|--------------|
| `mem_retain` | Retains a synthesized fact in workspace memory under a topic; retaining to an existing topic overwrites its fact. |
| `mem_recall` | Recalls previously retained facts via keyword search. |
| `mem_topics` | Discovers existing memory topics via hybrid (keyword + semantic) search over topic names; each hit includes the topic's latest fact. |

**Inquiries (`inq_*`)** — open an inquiry and search/read raw events as grounding:

| Tool | What it does |
|------|--------------|
| `inq_new` | Opens a new inquiry — a working set that accumulates events across searches and reads. Multiple inquiries can run concurrently to explore independent questions. |
| `inq_search` | Hybrid (keyword + semantic) search over indexed events, optionally re-ranked with a local cross-encoder model toward a natural-language question; new hits are added to the inquiry's working set. |
| `inq_read` | Reads events from a specific stream into the inquiry's working set. |
| `inq_view` | Shows the inquiry's working set, ordered chronologically. |
| `inq_forget` | Drops events from the inquiry; forgotten events stay excluded from future searches and reads for the inquiry's lifetime. |
| `inq_end` | Ends an inquiry and frees its resources (inquiries are otherwise closed automatically after 1 hour of inactivity). |

**Workspace (`ws_*`)** — inspect indexing, import data, and manage workspaces:

| Tool | What it does |
|------|--------------|
| `ws_status` | Reports the current workspace's indexing progress and the log head position. |
| `ws_streams` | Discovers streams in the workspace via hybrid (keyword + semantic) search over stream names. |
| `ws_import` | Returns ready-to-run instructions for bulk-importing data into the workspace over HTTP. |
| `ws_management` | Returns ready-to-run `curl` commands for managing workspaces (see [Workspaces](#workspaces)); requires admin or ops credentials. |

The [operational flags](#workspaces) on a workspace restrict these tools: `disableMemory` disables the memory tools, `disableInquiries` the inquiry tools, `disableImports` the bulk import, and `readOnly` all writes.

## Workspaces

A workspace is a sandbox for the AI agent — it scopes the agent to a particular subset of your data. Managing workspaces requires administrator or operations (`$ops`) privileges and is done through the control-plane HTTP API at `/kontext/workspaces` — whether by a human operator or by your own automation/tooling that holds the necessary credentials.

An AI agent can also manage workspaces itself through the `ws_management` tool, which returns ready-to-run `curl` commands — provided it has been given admin credentials. Note that an agent is bound to the workspace it connects to and cannot switch to another; connecting to a different workspace requires updating the agent's MCP configuration.

A workspace defines:

- **Filter rules** — stream-prefix patterns, each with an optional JavaScript expression (e.g. `rec => rec.value.organizationId === 'acme-corp'`), that decide which events get indexed. The `default` workspace indexes everything.
- **Operational flags** — optional toggles (all default to `false`) that restrict what agents can do in that workspace; a tool called against a forbidden operation returns a clear error:
  - `disableMemory` — disables the memory tools (`mem_*`), so agents can't save or recall facts.
  - `disableImports` — disables bulk import (`ws_import`).
  - `disableInquiries` — disables the inquiry tools (`inq_*`), so agents can't search or read raw events.
  - `readOnly` — blocks all writes (saving facts and importing data), leaving the workspace read-only.

### Create

`POST` to `<host>:<port>/kontext/workspaces`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/kontext/workspaces
Content-Type: application/json
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=

{
  "name": "acme-corp-support",
  "filterRules": [
    { "streamPrefix": "ticket-", "filter": "rec => rec.value.organizationId === 'acme-corp'" },
    { "streamPrefix": "conversation-", "filter": "rec => rec.value.organizationId === 'acme-corp'" }
  ]
}
```

- `filterRules` is a list of stream-prefix patterns.
- A `streamPrefix` matches every stream whose name starts with it (e.g. `ticket-` matches `ticket-123`); `""` matches every stream.
- `filter` is an optional JavaScript expression — omit it or set it to `null` to index every event matching the prefix.
- The operational flags (`disableMemory`, `disableImports`, `disableInquiries`, `readOnly`) are optional and default to `false`.

A newly created workspace is not started automatically — start it with the [Start](#start) request below to begin indexing.

### Start

`POST` to `<host>:<port>/kontext/workspaces/<name>/start`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/kontext/workspaces/acme-corp-support/start
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

### Stop

`POST` to `<host>:<port>/kontext/workspaces/<name>/stop`

e.g. with the default admin credentials:
```http
POST https://127.0.0.1:2113/kontext/workspaces/acme-corp-support/stop
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

While stopped, a workspace's indexing and search are paused.

### Delete

`DELETE` to `<host>:<port>/kontext/workspaces/<name>`

e.g. with the default admin credentials:
```http
DELETE https://127.0.0.1:2113/kontext/workspaces/acme-corp-support
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

The `default` workspace cannot be deleted.

### List

`GET` from `<host>:<port>/kontext/workspaces`

e.g. with the default admin credentials:
```http
GET https://127.0.0.1:2113/kontext/workspaces
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

### View

`GET` from `<host>:<port>/kontext/workspaces/<name>`

e.g. with the default admin credentials:
```http
GET https://127.0.0.1:2113/kontext/workspaces/acme-corp-support
Authorization: Basic YWRtaW46Y2hhbmdlaXQ=
```

## Deployment strategy

In production, run Kontext on a dedicated [read-only replica](../configuration/cluster.md#read-only-replica) node rather than on the cluster's regular nodes.

Enabling Kontext on every cluster node is wasteful: each node would independently index and embed the same events, duplicating the (CPU- and cost-heavy) embedding work. It would also compete for CPU, memory, and disk with the node's normal database duties, which can degrade cluster performance.

A read-only replica receives the full event stream through replication but does not take part in elections or quorum, so it can carry the indexing and embedding workload in isolation — without affecting the rest of the cluster. It can also be sized for Kontext's needs — for example, enough disk and RAM for the full-text and vector stores — independently of the other cluster nodes.

## Considerations

### Workspaces

#### Scoping

Creating a workspace is not cheap: each one subscribes to the transaction log and indexes the events it matches. Every indexed event is written to the full-text store and embedded into the vector store — consuming storage and CPU (and incurring API charges with a hosted embedding provider). The initial catch-up over a large log can also take a long time. Scope each workspace with filter rules so it indexes only the events you need, and choose the granularity carefully — neither so coarse that one workspace indexes far more than any agent needs, nor so fine that you end up managing a large number of tiny workspaces.

#### Default workspace

The `default` workspace indexes everything, so enable it with caution on a large database — indexing and embedding the entire log can be expensive. For production, prefer narrowly-scoped workspaces.

#### Shared memory

A workspace's memory (the facts saved via `mem_retain`) is, for now, shared by all users of that workspace. Treat a workspace as a single trust boundary — don't place users with differing permissions in the same workspace if its memory could combine data across those permissions; scope sensitive data into separate workspaces instead. We are considering, depending on feedback, adding finer scoping so that different users / chat sessions using the same workspace have isolated memories.

### Embeddings

#### Local embeddings are CPU-bound

The default in-process model runs on the server's CPU, so high event volumes mean slower catch-up and higher CPU usage; consider a hosted provider for large or high-throughput workloads.

#### Local embeddings truncate long events

The local model embeds at most 512 tokens per event; anything beyond that is dropped before embedding.

#### Embeddings are cached across workspaces

An event that matches more than one workspace is embedded only once; the shared cache reuses the result.

#### Vector storage must fit in memory

The embedding (vector) index does not currently support datasets larger than available RAM, so size scoped workspaces accordingly. We are planning to address this limitation, depending on user feedback.
