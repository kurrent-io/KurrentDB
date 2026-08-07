---
title: How Memory Systems Scope by Identity — who supplies user/project/workspace over MCP
type: research
date: 2026-07-21
author: sergio
tags: [kontext, mcp, identity, multi-tenancy, memory-systems]
---

## Question

Should Kontext model `workspace_id` / `project_id` / `user_id` as first-class concerns, given that an
LLM calling MCP tools cannot reliably supply them? How do existing memory systems and the MCP spec
solve the "who provides the scoping IDs" problem?

## Findings

### Per-system: where the scoping IDs come from

**Mem0** — data model: `user_id`, `agent_id`, `run_id`, `app_id`. SDK/Platform: application code
passes them as explicit parameters (`client.add(messages, user_id="alice")`); never LLM-generated.
OpenMemory MCP server (being sunset): user bound per-connection via the SSE URL path
(`/mcp/<client>/sse/<user-id>`), set once at install; server extracts it into Python `contextvars`
so every operation on that connection is scoped — the tools carry **no identity parameters at all**.
The archived `mem0-mcp` wrapper injects `MEM0_DEFAULT_USER_ID` from the server's env block. Hosted
Mem0 MCP binds via bearer token (`MEM0_API_KEY`) at config time.
Sources: docs.mem0.ai/core-concepts/memory-operations/add · mem0.ai/blog/introducing-openmemory-mcp ·
github.com/mem0ai/mem0-mcp · docs.mem0.ai/platform/mem0-mcp · github.com/mem0ai/mem0/issues/3855

**Zep** — data model: `user_id` (owns a per-user knowledge graph), `session_id`/`thread_id`,
`graph_id`/`group_id`. All application-supplied via server-side SDK; Zep recommends mapping your
internal auth user ID onto Zep's and warns that session-derived user IDs fragment a returning user's
history. Graphiti MCP server: namespace is **launch configuration** (`--group-id` CLI flag /
`GROUP_ID` env, default "main"); docs direct the human operator, not the model, to choose it. Tools
accept `group_id` only as a *filter*.
Sources: help.getzep.com/users · help.getzep.com/v2/memory ·
github.com/getzep/graphiti (mcp_server README) · help.getzep.com/graphiti/getting-started/mcp-server

**Letta (MemGPT)** — memory belongs to the *agent* (`agent_id`), created and routed by application
code. The LLM edits memory *contents* via tools (`core_memory_append`, `archival_memory_insert`)
but never chooses whose memory it operates on — that is fixed by which agent is running. Multi-user
= one agent per user (or shared blocks); user scoping reduces to the app's agent-routing decision.
Sources: docs.letta.com/api/resources/agents · docs.letta.com/tutorials/shared-memory-blocks

**LangMem / LangGraph store** — namespace tuples (`("memories", "{user_id}")`) declared by the
developer as a template; filled at invocation time from `config["configurable"]` / typed runtime
context. Explicit design statement: **"Agent just sees that it has memory. It doesn't know where
it's stored."**
Sources: langchain-ai.github.io/langmem/guides/dynamically_configure_namespaces ·
langchain-ai.github.io/langmem/reference/memory

**MCP specification** — servers are OAuth 2.1 resource servers; token audience-bound (RFC 8707);
token passthrough explicitly forbidden. Security Best Practices, on session hijacking: *"MCP Servers
MUST NOT use sessions for authentication"* and recommends `<user_id>:<session_id>` binding where
*"the user ID is derived from the user token and not provided by the client."* That is the normative
position: identity comes from the validated token, never from anything the client/model sends.
**Roots** are the spec-native project-context signal (workspace directories advertised by the
client) but are explicitly advisory — "SHOULD respect," not MUST — context scoping, not a security
boundary.
Sources: modelcontextprotocol.io/specification/2025-11-25/basic/authorization ·
modelcontextprotocol.io/specification/2025-06-18/basic/security_best_practices ·
modelcontextprotocol.io/specification/2025-06-18/client/roots

**Implicit-scope systems** — Claude Code auto-memory: scope key IS the git repo root path (worktrees
separate, cwd fallback); user is the OS account; zero identity parameters anywhere. ChatGPT memory:
tied to the authenticated account; in Business workspaces memories are per-account and
non-transferable. Pieces LTM: local-first, machine = user.
Sources: code.claude.com/docs/en/memory · help.openai.com/en/articles/8590148-memory-faq

**Hosted MCP memory servers — binding summary**

| Server | Mechanism | When bound |
|---|---|---|
| OpenMemory (Mem0) | user id in SSE URL path + server env | install/config |
| Mem0 hosted MCP | bearer token → account | config |
| Supermemory | unique secret URL per user; project via `x-sm-project` header | install (URL is the credential) |
| Pieces | local machine = user | implicit |
| Cognee | JWT with `user_id`, `tenant_id`, roles | config/token |
| Zep Graphiti | `--group-id` / `GROUP_ID` at launch | launch |

Supermemory's blog is explicit about the driver: they "nuke[d] auth completely" and generate a
unique URL per user, dynamically instantiating a server per user — even the auth-less design binds
identity at the transport layer, never as a tool argument.

### Named patterns

- **Ambient tenant context** — token claim → middleware → context variable consulted implicitly by
  the data layer. OWASP Multi-Tenant Security Cheat Sheet: "derive tenant context from
  authenticated, verified tokens"; "do not trust tenant IDs from client headers or request
  parameters" (a tool argument is a client-supplied request parameter that happened to pass through
  an LLM).
- **Per-connection binding** — identity attached once at connection establishment (URL, token, env),
  inherited by all tool calls.
- **Fail-closed data layer** — WorkOS: "the most common real-world failure in multi-tenant apps is
  not a bad token, it is a missing WHERE clause"; require tenant_id to construct any query, plus RLS
  so a forgotten filter fails closed.
- **Multi-project** — handled by per-project server instances or per-project headers, not per-call
  arguments.

### Are identity fields ever LLM-supplied?

Almost never as the trust-bearing value. Where `userId`-style tool args exist (community wrappers),
they immediately grow `DEFAULT_USER_ID` env fallbacks — evidence the model has no way to know the
right value and forgets or invents it. Trusting a model-supplied `user_id` is a textbook confused
deputy / IDOR.

## Implications

Kontext's settled v3 position (`resources.proto` well-known Tag scopes) matches the industry
consensus exactly:

1. **`user` stays out of the tool schemas** — ambient from auth (OAuth `sub` / trusted header / git
   or OS user on stdio), server-stamped and enforced. Never an agent-typed field.
2. **`project`/`workspace` are advisory context** — supplied by the client side deterministically
   (per-project server config in `.mcp.json`, MCP roots as hint, host-derived tags), not by the
   model; misfiling is recoverable, so advisory is acceptable.
3. **Enforce in the data layer, fail closed** — every store query must require the principal. This
   is the argument that also settles the storage question: the LanceDB prefilter constraint (no
   prefilter over `VARCHAR[]`, scalars only) means the isolation dimension must be a promoted scalar
   column (`user`, plus `session`/`project`/`workspace`) in the `memories` Lance table, with the
   server prepending `user = @principal`. Wire contract unchanged — promotion happens at the store
   boundary, tags fold back on read. Well-known scopes become single-valued per memory (validator
   enforced).
