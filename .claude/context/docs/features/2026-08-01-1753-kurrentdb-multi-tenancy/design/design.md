---
title: KurrentDB Multi-Tenancy
status: exploring        # exploring | settling | superseded
authors: [sergio]
date: 2026-08-01
tags: [multi-tenancy, auth, identity, streams, policies, security]
---

# Design Space — KurrentDB Multi-Tenancy

> **Part I** is the clean current state — conclusions only, no history.
> **Part II** is the changelog: decision log, deliberation, rejected options, evidence.

> **CLOSURE (2026-08-01, operator-ratified): engine-level tenancy is PARKED** — a fully
> designed future option, not current work. Outcome of the session: **Capacitor is the org
> authority** and implements multi-tenancy at the application level (AI-1558 roadmap unchanged;
> AI-1575's metadata stamping is the correct mechanism in that frame). **Kontext** treats org
> dimensions as tags (the settled v3 contract), owns **repositories** as its one native entity,
> and grows an entity-connector mechanism — continued in
> `docs/features/2026-08-01-2328-kontext-entity-connectors/`. Part I below remains the parked
> option's spec-seed; the T1/T2 growth path stays open by design.

---

# PART I — CONCLUSIONS (current state)

## 1. Problem

- KurrentDB has no tenancy primitive: one flat stream namespace, one user store, one `$all`,
  one secondary index per node.
- Isolation today = naming discipline + hand-maintained ACLs/policies.
- Wanted: an org structure (tenant → workspace → project) that Kontext and Capacitor both
  build on, with a growth path to engine-level tenancy (T1/T2) later.

## 2. Org structure

### 2.1 The structure (product-agnostic)

The engine defines exactly two levels — tenant and workspace. Everything below the workspace is
application-defined naming; the engine sees only names and prefixes.

```text
KurrentDB node
│
├── (unmarked streams) ────────────────────►  tenant "default" — legacy data, zero change
│
└── {tenant-key}::                            TENANT                   [engine registry · HARD]
    │
    ├── {entity-type}/{id}                    tenant-root entities — span workspaces
    │   └── …/{facet}
    │
    ├── {workspace}/                          WORKSPACE                [registry existence · soft]
    │   └── {entity-type}/{id}                workspace entities — app-defined
    │       └── …/{facet}
    │
    └── {workspace}/                          …as many workspaces as the tenant registers
```

```text
ENGINE (registry)     tenants 1 ──── N workspaces
                      tenants.key      ──► stream prefix  "{key}::"
                      workspaces.slug  ──► path segment   "{key}::{slug}/"

APPLICATIONS          define every entity type and facet below those two levels;
                      memberships, hierarchies (projects, folders, …) and federated
                      identity are application data — invisible to the engine
```

| Level         | Managed by                                     | In KurrentDB                | Boundary                              |
|---------------|------------------------------------------------|-----------------------------|---------------------------------------|
| **Tenant**    | KurrentDB registry (key, display name, status) | name prefix `{key}::`       | HARD — isolation; promotable to T1/T2 |
| **Workspace** | existence in registry; semantics app-side      | path level `{key}::{slug}/` | soft — registered, enforceable later  |
| **(below)**   | fully app-side                                 | path/tag content only       | none — scoping dimensions             |

### 2.2 Example — Capacitor under the structure

Tenant `a7k3f9` ("ACME Corp"), workspaces `platform` and `mobile`; projects stay app-side
aggregates (a repo attaches to N projects, so project is never a data path level):

```text
a7k3f9::                                      TENANT "ACME Corp"
│
├── team/01K1GZ3H7R                           tenant-root: workspace-team aggregate
├── user/a3f0afb8                             tenant-root: one user, N workspaces
│
├── platform/                                 WORKSPACE "Platform"
│   ├── project/01K1H0XQ2M                    project aggregate (scoping, app-side)
│   │   └── …/settings                          facet
│   ├── session/01k1gz59weftq
│   ├── agent-run/01K1HW2J8B
│   │   └── …/input                             facet
│   └── repo/3fa9c04d7be2
│       └── …/judge-facts                       facet
│
└── mobile/                                   WORKSPACE "Mobile"
    └── …
```

App-side relationships (Capacitor data, never engine concepts): `workspaces 1—N projects` ·
`projects N—M repos` (`project_repos`) · `projects N—M users` (`project_members`) · user
identity from WorkOS/GitHub.

### 2.3 Example — Kontext memories under the structure

Kontext is platform — it lives inside KurrentDB, so its data streams are SYSTEM streams
(operator-set, 2026-08-01): the tenant marker goes inside the `$` namespace.

```text
$a7k3f9::kontext/platform/memory/01K1J8M4TS   the memory's event stream (lineage appends here)
$kontext/platform/memory/01K1J8M4TS           same, on an untenanted node (tenant "default")
```

- **General rule:** tenant-scoped system data = `$` + `{key}::` + path. The untenanted form is
  the same name without the marker — today's `$kontext/…` shapes generalize by inserting
  `{key}::` after the `$`.
- **Workspace is in the name** (operator-set): the service owns its internal layout —
  `kontext/{workspace}/memory/{id}` — so tenant AND workspace are name-parsed, and per-workspace
  memory slices are prefixes (`$a7k3f9::kontext/platform/`).
- **`$` does the reservation:** applications write `a7k3f9::…` user streams and can never
  collide with `$a7k3f9::…` platform data (e.g. Capacitor's own `…/memory/{id}` aggregate is
  disjoint from Kontext's `$…::kontext/memory/{id}`).
- **System ACLs make Kontext's API the only door:** raw memory streams are admin/system-scoped
  by the default system-stream ACL; tenants reach memories exclusively through the Kontext
  service, which enforces lanes.
- **Ownership derivation still holds:** strip `$` decorations, re-match `^[a-z0-9]{6}::` — the
  same rule that already covers `$$a7k3f9::…` and `$ce-a7k3f9::…`.
- Replaces today's single global `$kontext/memories` stream — a cross-tenant blender no prefix
  slice or enforcement could ever cover. The projector consumes a filtered `$all` subscription
  by Kontext event-type prefix instead (log-position ordering unchanged).

```text
Lance `memories` row for that stream:

  tenant    = 'a7k3f9'      isolation column — fail-closed; PARSED FROM THE NAME
  workspace = 'platform'    isolation column — parsed from the name / claim-checked
  tags      = ['project:checkout', 'repo:3fa9c04d7be2']    scoping tags — advisory, queryable
```

Isolation dimensions become columns; scoping dimensions (project, repo) stay tags. Kontext
never manages projects — it stores what applications tag.

## 3. Naming convention (SETTLED)

```text
{tenant-key}::{workspace}/{entity-type}/{id}[/{facet}]
```

| Rule                     | Value                                                                    |
|--------------------------|--------------------------------------------------------------------------|
| Tenant key               | `^[a-z0-9]{6}$` — server-issued, unique by registry check, **immutable** |
| Tenant-stream classifier | `^[a-z0-9]{6}::` — the one-line "is this a tenant stream" test           |
| Workspace slug           | `[a-z0-9][a-z0-9-_]*` — path segment                                     |
| Entity types             | lower-kebab-case (`agent-run`, `project`)                                |
| Facets                   | per-entity sub-streams: `…/{type}/{id}/{facet}`                          |
| Unmarked names           | tenant `default` — zero behavior change for existing data                |
| Local part               | `$` forbidden as first char (hygiene)                                    |
| Platform/system data     | `$` + `{key}::` + path — e.g. `$a7k3f9::kontext/memory/{id}`; the `$` reserves the space, apps cannot collide |
| Rename                   | display names live in the registry; names in the log never change        |

Slices are prefixes — no dashes, no categories required:

| Want                                      | Prefix                                |
|-------------------------------------------|---------------------------------------|
| Tenant `$all`                             | `a7k3f9::`                            |
| Workspace slice                           | `a7k3f9::platform/`                   |
| Entity feed (all projects in a workspace) | `a7k3f9::platform/project/`           |
| Entity node (everything about one repo)   | `a7k3f9::platform/repo/3fa9c04d7be2/` |

Served by filtered `$all` reads/subscriptions (`EventFilter.StreamName.Prefixes`), stream
policies (prefix match), and index `LIKE 'prefix%'` predicates.

## 4. Schemas

### 4.1 Engine registry — DuckDB (in-memory node replica)

Source of truth is the `$tenants` / `$tenant-{key}` event streams (§4.3). Every node rebuilds
this replica by subscription on startup (the policy-registry pattern).

```sql
-- The COMPLETE engine-side data model. Growth past these two tables means the
-- engine/application boundary is leaking.

CREATE TYPE tenant_status    AS ENUM ('active', 'suspended');
CREATE TYPE workspace_status AS ENUM ('active', 'archived');

CREATE TABLE tenants (
    -- Immutable six-character key. Appears as the "{key}::" prefix in stream names.
    key          TEXT PRIMARY KEY CHECK (regexp_full_match(key, '[a-z0-9]{6}')),

    -- Human-facing name. Renameable at any time; never part of a stream name.
    display_name TEXT          NOT NULL,

    -- 'suspended' rejects appends into the "{key}::" namespace.
    status       tenant_status NOT NULL DEFAULT 'active',

    -- Opaque pointer to the owning external system (WorkOS org, GitHub org, ...).
    -- Stored, listed, never interpreted.
    external_ref TEXT,

    created_at   TIMESTAMPTZ   NOT NULL
);

CREATE TABLE workspaces (
    tenant_key   TEXT             NOT NULL REFERENCES tenants (key),

    -- Path segment: streams live under "{tenant_key}::{slug}/".
    slug         TEXT             NOT NULL CHECK (regexp_full_match(slug, '[a-z0-9][a-z0-9-_]*')),

    display_name TEXT             NOT NULL,
    status       workspace_status NOT NULL DEFAULT 'active',
    created_at   TIMESTAMPTZ      NOT NULL,

    PRIMARY KEY (tenant_key, slug)
);
```

### 4.2 Management API — `tenancy_service.proto`

```protobuf
// KurrentDB tenancy management API.
//
// Manages the org-structure registry: tenants (the hard namespace boundary,
// projected as the "{key}::" stream-name prefix) and workspace existence
// (registered path levels below a tenant). Richer semantics — members, seats,
// roles, projects — are application concerns and are deliberately absent
// from this contract.
//
// All operations are admin-gated through the node's authorization policies
// (the Operations.Tenants group).
syntax = "proto3";

package kurrentdb.protocol.v2.tenancy;

import "google/protobuf/timestamp.proto";

option csharp_namespace = "KurrentDB.Protocol.V2.Tenancy";

service TenancyService {
  // Creates a tenant. The server issues the immutable six-character key.
  rpc CreateTenant(CreateTenantRequest) returns (CreateTenantResponse);

  // Returns one tenant by key.
  rpc GetTenant(GetTenantRequest) returns (GetTenantResponse);

  // Lists tenants, paged.
  rpc ListTenants(ListTenantsRequest) returns (ListTenantsResponse);

  // Changes the display name. The key and existing stream names never change.
  rpc RenameTenant(RenameTenantRequest) returns (RenameTenantResponse);

  // Suspends the tenant. Appends into "{key}::" are rejected while suspended.
  // There is no delete: streams are immutable.
  rpc SuspendTenant(SuspendTenantRequest) returns (SuspendTenantResponse);

  // Lifts a suspension.
  rpc ResumeTenant(ResumeTenantRequest) returns (ResumeTenantResponse);

  // Registers a workspace under a tenant. Existence only: members, seats,
  // and roles belong to applications.
  rpc CreateWorkspace(CreateWorkspaceRequest) returns (CreateWorkspaceResponse);

  // Lists the workspaces of a tenant.
  rpc ListWorkspaces(ListWorkspacesRequest) returns (ListWorkspacesResponse);

  // Changes a workspace display name. The slug never changes.
  rpc RenameWorkspace(RenameWorkspaceRequest) returns (RenameWorkspaceResponse);

  // Archives a workspace. Existing streams under the slug remain readable.
  rpc ArchiveWorkspace(ArchiveWorkspaceRequest) returns (ArchiveWorkspaceResponse);
}

// A tenant registry entry. The engine stores nothing else about a tenant.
message Tenant {
  // Immutable six-character key (^[a-z0-9]{6}$), server-issued.
  // Appears as the "{key}::" prefix in stream names.
  string key = 1;

  // Human-facing name. Renameable; never part of a stream name.
  string display_name = 2;

  TenantStatus status = 3;

  // Opaque reference to the owning external system, for example a WorkOS
  // organization id or a GitHub organization id. Never interpreted.
  optional string external_ref = 4;

  google.protobuf.Timestamp created_at = 5;
}

enum TenantStatus {
  TENANT_STATUS_UNSPECIFIED = 0;
  TENANT_STATUS_ACTIVE      = 1;

  // Appends into the tenant namespace are rejected while suspended.
  TENANT_STATUS_SUSPENDED   = 2;
}

// A registered workspace: a path level under a tenant. Existence only.
message Workspace {
  // Key of the owning tenant.
  string tenant_key = 1;

  // Path segment ([a-z0-9][a-z0-9-_]*). Streams live under
  // "{tenant_key}::{slug}/".
  string slug = 2;

  // Human-facing name. Renameable; the slug never changes.
  string display_name = 3;

  WorkspaceStatus status = 4;

  google.protobuf.Timestamp created_at = 5;
}

enum WorkspaceStatus {
  WORKSPACE_STATUS_UNSPECIFIED = 0;
  WORKSPACE_STATUS_ACTIVE      = 1;
  WORKSPACE_STATUS_ARCHIVED    = 2;
}

message CreateTenantRequest {
  string display_name = 1;
  optional string external_ref = 2;
}

message CreateTenantResponse {
  Tenant tenant = 1;
}

message GetTenantRequest {
  string key = 1;
}

message GetTenantResponse {
  Tenant tenant = 1;
}

message ListTenantsRequest {
  // Maximum entries to return; the server may return fewer.
  int32 page_size = 1;

  // Token from a previous ListTenantsResponse; empty for the first page.
  string page_token = 2;
}

message ListTenantsResponse {
  repeated Tenant tenants = 1;

  // Empty when there are no more pages.
  string next_page_token = 2;
}

message RenameTenantRequest {
  string key = 1;
  string display_name = 2;
}

message RenameTenantResponse {
  Tenant tenant = 1;
}

message SuspendTenantRequest {
  string key = 1;
}

message SuspendTenantResponse {
  Tenant tenant = 1;
}

message ResumeTenantRequest {
  string key = 1;
}

message ResumeTenantResponse {
  Tenant tenant = 1;
}

message CreateWorkspaceRequest {
  string tenant_key = 1;
  string slug = 2;
  string display_name = 3;
}

message CreateWorkspaceResponse {
  Workspace workspace = 1;
}

message ListWorkspacesRequest {
  string tenant_key = 1;
}

message ListWorkspacesResponse {
  repeated Workspace workspaces = 1;
}

message RenameWorkspaceRequest {
  string tenant_key = 1;
  string slug = 2;
  string display_name = 3;
}

message RenameWorkspaceResponse {
  Workspace workspace = 1;
}

message ArchiveWorkspaceRequest {
  string tenant_key = 1;
  string slug = 2;
}

message ArchiveWorkspaceResponse {
  Workspace workspace = 1;
}
```

### 4.3 Registry events — `tenancy_events.proto`

```protobuf
// Registry events, appended to the $tenants and $tenant-{key} system streams.
// These streams are the source of truth; the DuckDB tables in the node are a
// rebuildable replica.
syntax = "proto3";

package kurrentdb.protocol.v2.tenancy.events;

import "google/protobuf/timestamp.proto";

option csharp_namespace = "KurrentDB.Protocol.V2.Tenancy.Events";

// A tenant was created and its immutable key issued.
message TenantCreated {
  string key = 1;
  string display_name = 2;
  optional string external_ref = 3;
  google.protobuf.Timestamp created_at = 4;
}

// The display name changed. The key never changes.
message TenantRenamed {
  string key = 1;
  string display_name = 2;
}

// Appends into "{key}::" are rejected from this point.
message TenantSuspended {
  string key = 1;
  google.protobuf.Timestamp suspended_at = 2;
}

// The suspension was lifted.
message TenantResumed {
  string key = 1;
  google.protobuf.Timestamp resumed_at = 2;
}

// A workspace was registered under the tenant.
message WorkspaceCreated {
  string tenant_key = 1;
  string slug = 2;
  string display_name = 3;
  google.protobuf.Timestamp created_at = 4;
}

// The workspace display name changed. The slug never changes.
message WorkspaceRenamed {
  string tenant_key = 1;
  string slug = 2;
  string display_name = 3;
}

// The workspace was archived. Existing streams remain readable.
message WorkspaceArchived {
  string tenant_key = 1;
  string slug = 2;
  google.protobuf.Timestamp archived_at = 3;
}
```

### 4.4 App-side reference — Capacitor (verified; the engine stores NONE of this)

Field-class legend: `[naming]` existence/identity by name · `[linkage]` pointer into another
system · `[status]` lifecycle · `[domain]` application semantics · `[infra]` deployment.

```sql
-- TENANT, app view. AuthProxy SQLite (src/Capacitor.AuthProxy/TenantDb.cs — verbatim shape).
CREATE TABLE IF NOT EXISTS tenants (
    org_id              INTEGER PRIMARY KEY,        -- GitHub/WorkOS account id      [linkage]
    hostname            TEXT    NOT NULL UNIQUE,    -- cell routing                  [infra]
    origin              TEXT    NOT NULL,           --                               [infra]
    installation_id     INTEGER,                    -- GitHub App install            [linkage]
    org_login           TEXT,                       --                               [linkage]
    suspended           INTEGER NOT NULL DEFAULT 0, --                               [status]
    installer_github_id INTEGER,                    -- bootstrap-admin provenance    [linkage]
    installer_login     TEXT,                       --                               [linkage]
    account_type        TEXT                        -- 'Organization' | 'User'       [linkage]
);
-- Plan class (Free|Team|Enterprise|Internal) + TrialEndsAt: ENV-ONLY, deliberately in no DB.

-- PROJECT read model. Postgres (src/Capacitor.Server/ReadModels/ProjectProjector.cs;
-- write side is the Project-{id} event-sourced aggregate in KurrentDB).
CREATE TABLE IF NOT EXISTS projects (
    project_id    TEXT PRIMARY KEY,                 --                               [naming]
    slug          TEXT,                             --                               [naming]
    name          TEXT,                             --                               [naming]
    description   TEXT,                             --                               [domain]
    owner_user_id TEXT,                             --                               [domain]
    confirmed     BOOLEAN,                          --                               [status]
    archived      BOOLEAN,                          --                               [status]
    created_at    TIMESTAMPTZ,
    log_position  BIGINT
);

CREATE TABLE IF NOT EXISTS project_repos (
    project_id TEXT, repo_hash TEXT, repo_slug TEXT, log_position BIGINT,
    PRIMARY KEY (project_id, repo_hash)             -- repo ∈ N projects             [domain]
);

CREATE TABLE IF NOT EXISTS project_members (
    project_id TEXT, member_kind TEXT, member_id TEXT, display_name TEXT, log_position BIGINT,
    PRIMARY KEY (project_id, member_kind, member_id) --                              [domain]
);

-- USER. Postgres (src/Capacitor.Server/ReadModels/CapacitorDb.cs:1374 — verbatim shape).
-- Identity comes from WorkOS/GitHub, never from KurrentDB.
CREATE TABLE IF NOT EXISTS users (
    user_id              TEXT PRIMARY KEY NOT NULL, --                               [linkage]
    github_id            BIGINT UNIQUE,             --                               [linkage]
    username             TEXT NOT NULL,             --                               [naming]
    email                TEXT NOT NULL,             --                               [domain]
    avatar_url           TEXT,                      --                               [domain]
    display_name         TEXT,                      --                               [naming]
    registered_at        TIMESTAMPTZ NOT NULL,
    cli_setup_at         TIMESTAMPTZ,               --                               [domain: UI state]
    welcome_dismissal    TEXT,                      --                               [domain: UI state]
    welcome_dismissed_at TIMESTAMPTZ                --                               [domain: UI state]
);

-- REPO. Postgres (CapacitorDb.cs:1209 — verbatim shape).
CREATE TABLE IF NOT EXISTS repositories (
    repo_hash       TEXT PRIMARY KEY,               --                               [naming]
    owner           TEXT NOT NULL,                  --                               [domain]
    repo_name       TEXT NOT NULL,                  --                               [naming]
    latest_activity TIMESTAMPTZ NOT NULL,           --                               [domain]
    log_position    BIGINT NOT NULL
);
```

Workspace/Team is NOT a table — it is an event-sourced aggregate in KurrentDB
(`TeamState.cs:8-11` + AI-1574): `team_id [naming] · name [naming] · members [domain] ·
deleted [status]` + owner, seat cap, WorkOS org↔team 1:1 mapping; roles come from the
`kapacitor:role` claim.

**The three-class rule:**

| Class               | Examples                                       | Lives at                                                       |
|---------------------|------------------------------------------------|----------------------------------------------------------------|
| 1. naming/existence | key, slug, display name, status                | **engine registry** (tenant + workspace only, ~7 fields total) |
| 2. linkage          | org_id, installation_id, github_id, WorkOS ids | integration owner; engine gets one opaque `external_ref`       |
| 3. domain/state     | members, seats, repos, descriptions, UI state  | application                                                    |

## 5. Minimum KurrentDB work (v1) — PROPOSAL FOR AGREEMENT

Premise (operator): fine-grained read scoping is APP-controlled (Capacitor→Postgres,
Kontext→DuckDB/Lance). KurrentDB v1 makes the org structure **exist, managed, trusted** — not
per-principal enforced.

| # | IN (v1)                                                                                                | Size                    |
|---|---------------------------------------------------------------------------------------------------------|-------------------------|
| 1 | Tenant registry — `$tenants` + `$tenant-{key}` streams, in-memory node replica                          | small                   |
| 2 | Key issuance — `^[a-z0-9]{6}$`, registry uniqueness, reserved words                                     | small                   |
| 3 | Management API (§4.2) — gated by existing ops/policy (`Operations.Tenants`, admin-only)                 | medium                  |
| 4 | Append-path validation — classifier match ⇒ tenant registered + ACTIVE; unmarked names bypass           | small, O(1)             |
| 5 | Namespace-aware category derivation (Part II, C9, option C) — two functions                             | small; cuttable to v1.1 |

**OUT (designed, deferred):** per-principal tenant assertion · claims minting · `$all` filter
injection · `$idx-` predicate injection · policy auto-scoping · per-tenant users · quotas.

**Auth position:**

- Data path: deliberately not enforced in v1 — consumers are trusted principals (Capacitor =
  sole principal per cell; Kontext = in-process). Cooperative-namespace positioning.
- Management API: enforced via the existing operations/policy gate.
- The enforcement phase composes later without rework — everything keys on the classifier.

**Sufficiency per consumer:**

- Capacitor: provision tenant at signup (subsumes key half of AI-1574's registry) · switch
  names at the single append funnel · keep AI-1575 query wraps · per-tenant subscriptions =
  prefix-filtered `$all`.
- Kontext: tenant key from the same registry · lanes = namespace + promoted columns · MCP-auth
  claim maps to `{key}` + workspace segment.

## 6. Layer rationale — where boundaries meet

| Layer                                                           | Owns                                                          | Test                                                           |
|-----------------------------------------------------------------|---------------------------------------------------------------|----------------------------------------------------------------|
| **Engine**                                                      | tenant (behavioral), workspace (existence)                    | changes how the engine stores/filters/validates/authorizes?    |
| **Platform** (Kontext, SchemaRegistry — in-process ≠ in-engine) | app concepts as DATA dimensions (columns, tags, names)        | only selects/scopes data?                                      |
| **Application** (Capacitor, MCP clients)                        | full domain: projects, repos, members, seats, federated users | needs existence/listing/rename? → registry AT THE OWNING LAYER |

- **Isolation vs scoping**: tenant/workspace ISOLATE (fail-closed, enforceable, T1-promotable);
  project/repo SCOPE (advisory, queryable, never engine-enforced).
- **Identity**: users-as-people → IdPs + apps; users-as-principals → engine (service account +
  claims).
- **API noun boundaries**:

| API               | May say                                                      | Must never say                   |
|-------------------|--------------------------------------------------------------|----------------------------------|
| Engine management | tenant, workspace(-existence), stream, policy                | project, repo, user-profile      |
| Kontext memory    | memory; ambient tenant/workspace; project/repo as tag values | project/repo as managed entities |
| Capacitor         | everything                                                   | —                                |

## 7. Open Questions

> PARKED 2026-08-01 with the engine option (see closure banner and C1). These reopen if and
> when engine-level tenancy is picked up; none block Kontext or Capacitor.

- **Ratify the reframe** — cooperative namespace isolation as the feature's positioning.
- **Ratify the minimum-work proposal** (§5) — includes registry residence (`$tenants` streams).
- **Membership cardinality** — exactly-one namespace per principal vs claim set.
- **Grandfathering** — legacy names matching `^[a-z0-9]{6}::` on enablement: block / quarantine
  admin-only / absorb into `default`.
- Category derivation option C — ratify (two patches) or cut to v1.1.
- Per-tenant `$et-` variants — ever? What triggers the investment?
- Index detail (enforcement phase): tenant-column backfill; claim→predicate dispatch point.
- Login-name uniqueness per node vs per tenant (enforcement phase).
- Entitlement: license-gated (commercial plugin) or core?

---

# PART II — CHANGELOG & DELIBERATION RECORD

## C1. Decision log

- 2026-08-01 — Target: core KurrentDB tenancy; pre-agreed drop-down to Kontext
  tenant-above-workspace if too big. (Drop-down not triggered.)
- 2026-08-01 — Identity rules inherited from the identity-scoping research: tenant ambient from
  the principal, never client-supplied, fail-closed.
- 2026-08-01 — **Naming settled**: `{tenant-key}::{free-form-local}`; key `^[a-z0-9]{6}$`,
  registry-issued, immutable; display names in registry; unmarked = `default` tenant.
  Rejected: URN (dead constant bytes; engine can't use structure) · unmarked `/`-paths (first
  segment unidentifiable) · readable-slug keys (rename impossible — names immutable forever).
- 2026-08-01 — **Final local form**: kebab-case paths `{key}::{ws}/{type}/{id}[/{facet}]`;
  facets replace flat settings/permissions categories; project = aggregate, not a path level.
- 2026-08-01 — **T1-readiness invariant** (operator): all behavior keys on the classifier;
  unmarked streams byte-for-byte unchanged; nothing forecloses T2→T1.
- 2026-08-01 — Terminology stays **"tenant"**, on condition the T2 growth path stays open.
- 2026-08-01 — **Platform data streams are system streams, workspace included**:
  `$` + `{key}::` + service path — `$a7k3f9::kontext/{workspace}/memory/{id}`; untenanted form
  `$kontext/{workspace}/memory/{id}`. Rejected same day: non-`$` platform streams with a
  reserved `kontext/` lane segment (the `$` already reserves the space and keeps platform data
  behind system ACLs), and workspace-as-column-only (name-parsed beats payload-trusted, and
  workspace slices become prefixes).
- 2026-08-01 — **CLOSURE (operator-ratified). Engine-level tenancy PARKED.** The out-of-the-box
  pass (app as root isolation factor; two apps competing for the same concepts) landed the
  concerns at the layers that own the reasons for them:
  - **Capacitor = org authority** — multi-tenancy is its business requirement, implemented at
    the app level; AI-1558/1574/1575 proceed unchanged, and AI-1575's metadata stamping is the
    correct mechanism in this frame, not a workaround.
  - **Engine = nothing now** — registry, naming enforcement, management API all parked as this
    document's Part I; T1/T2 path intact.
  - **Kontext** — org dimensions (tenant, workspace, even repo-id on memories) are TAGS,
    confirming the settled v3 scope-as-tags contract; **repositories** are its one native
    entity (it imports and curates history itself); enforced dimensions stay promoted columns
    at the store boundary per the identity-scoping research.
  - **Entity connectors** (operator concept): Kontext owns the NER/linking MECHANISM; apps
    supply the VOCABULARY through a registration/connector API — Kontext never imports app
    nouns as concepts, only as data. Capacitor is a driver, not the only one. Continued in
    `docs/features/2026-08-01-2328-kontext-entity-connectors/`.
  - Also ratified in the closing discussion: Capacitor MAY hard-depend on Kontext, and
    Capacitor's own memories feature (AI-1134) converges into Kontext when Kontext is ready.

## C2. Axes tenancy cuts through

identity · user management · stream addressing · read paths (`$all`, categories, index) ·
policy enforcement · system streams/projections · admin/ops surface · cluster (replication,
scavenge, quotas).

## C3. Isolation models

| Model | Shape                                              | Verdict                                   |
|-------|----------------------------------------------------|-------------------------------------------|
| T0    | policy-only convention                             | subset of T1 provisioning; `$all` leaks   |
| T1    | namespace tenancy, claim-derived, server-validated | working target; validation-not-rewriting  |
| T2    | `tenant_id` in the log record                      | growth path; log migration, multi-release |
| T3    | virtual databases per tenant                       | different product architecture            |

- Sizing verdict: T1-validation deliverable; every ingredient has a seam (C5).
  Scope-explosion triggers: transparent name rewriting · per-tenant system projections ·
  per-tenant login namespaces · per-tenant quotas/scavenge.
- Drop-down criterion (never triggered): T1 degenerates into T2, entry paths uncloseable, or
  system projections unforkable → Kontext tenant-above-workspace.

## C4. Verified machinery anchors

| Fact                                                                              | Evidence                                                                                                                                         |
|-----------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| Filtered `$all` prefix machinery is native (multi-prefix)                         | `EventFilter.cs:28`, `StorageReaderWorker.AllFiltered.cs`, `SystemClient.cs:102`                                                                 |
| Auth providers are plugins; principal stamped once                                | `IAuthenticationPlugin.cs:6-15`, `ClusterVNodeHostedService.cs:246`, `AuthenticationMiddleware.cs:78-90`                                         |
| One authz contract; assertion chains                                              | `IAuthorizationProvider.cs:8-12`, `PolicyAuthorizationProvider.cs:22-28`, `IAssertion.cs`                                                        |
| Operations carry StreamId at every decision point (transport + service)           | `Operation.cs:8-11`, `Streams.Append.cs:43-46`, `Streams.Read.cs:61-69`, `StreamsService.cs:294-296`                                             |
| Users = `$user-{login}` + `$users`; groups in `UserData.Groups`; admin-gated CRUD | `UserManagementService.cs:278-279,386,405,465-565,567-568`, `AllUsersReader.cs:27-68`                                                            |
| ACL 3-tier fallback; policy mode data-driven via `$authorization-policy-settings` | `StreamAcl.cs:11-34`, `LegacyStreamPermissionAssertion.cs:116-139`, `StreamBasedAuthorizationPolicyRegistry.cs:22-124`                           |
| Prefix-policy assertion runs in production                                        | `StreamPolicyAssertion.cs:15-82`                                                                                                                 |
| `$by_category` separator: first/last of one char; default `"first\r\n-"`          | `StreamCategoryExtractor*.cs`, `ProjectionManager.cs:894,901`                                                                                    |
| Index category = first-dash, hardcoded                                            | `DefaultIndexProcessor.cs:128-131`                                                                                                               |
| `$idx-*` virtual streams + DuckDB reader + IndexSubscription                      | `.claude/docs/streams-read-grpc.md` §3/§7.3/§8.4, `EventTypeSql.cs`                                                                              |
| Filters are `oneof` — stream-name × event-type CANNOT compose                     | `.claude/docs/streams-read-grpc.md` §4                                                                                                           |
| Stream name serialized per record (byte cost is real)                             | `PrepareLogRecord.cs:88,100,110`                                                                                                                 |
| `$` = system, `$$` = metastream                                                   | `SystemNames.cs:40,42,59,70`                                                                                                                     |
| Stream ids ride URL paths with trailing segments                                  | `AtomController.cs:130-142`, `StreamDetail.razor`, `EventDetail.razor` — objection later withdrawn (operator: `/` in production names, no issue) |

## C5. Sizing: every T1 ingredient has a seam

| Ingredient             | Seam                                                | Nature                       |
|------------------------|-----------------------------------------------------|------------------------------|
| Tenant claim minting   | auth plugins + group→claim (Kontext precedent)      | addition                     |
| Membership storage     | `UserData.Groups` (`$tenant:<key>`)                 | addition, zero schema change |
| Enforcement            | new assertion; `StreamPolicyAssertion` is the shape | addition (policy mode)       |
| Tenant `$all`          | native prefix filters                               | constraint injection         |
| Registry               | `$tenants` streams                                  | addition, small              |
| Cross-tenant ops       | node-global `admin`/`ops`                           | free                         |
| Tenant-admin user CRUD | loosen `IsAdmin` gate                               | modification, contained      |

Cost drivers: entry-path enumeration completeness · system-projection scoping · user
projections/persistent subscriptions stay admin-only in v1.

## C6. Naming evolution (decision trail)

1. **Flat slug** `acme::orders-1042` — first proposal; `::` avoids the category-separator
   collision (`-`); dash-free tenant ids.
2. **URN** (`urn:kurrent:tenant:acme:…`) — REJECTED: constant scheme = dead bytes on every
   record (name serialized per prepare); engine matches strings, cannot use structure; derived
   streams unwieldy; hierarchy YAGNI. Revisit if T2 makes names pure convention.
3. **Path idea** (operator): `{tenant}/{path}` — structure better than flat (subtree policies
   free). URL-routing objection raised, then WITHDRAWN (operator: `/` already in production).
4. **Identification problem** (operator): first segment must be recognizable as tenant →
   `::` marker + **tiny immutable key** (rename argument: names burn into the log forever;
   readable slugs make tenant rename impossible). REJECTED: sigil markers, readable-slug keys.
5. **SETTLED**: `{key}::…`, key `^[a-z0-9]{6}$` (operator: "6 digits, regex-validatable" —
   interpreted as lowercase alnum).
6. **Worked example** against Capacitor's real inventory → placement rule (workspace = path
   level, project = aggregate); interim "no dash in segments" rule.
7. **Final form** (operator): kebab-case `{key}::{ws}/{type}/{id}[/{facet}]` → facets; interim
   dash rule RETIRED; category machinery question → C9.

## C7. System streams — three classes

| Class                    | Examples                                                          | Rule                                                                                                                 |
|--------------------------|-------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------|
| Tenant-derived           | `$$a7k3f9::…`, `$ce-a7k3f9::…`, persistent-sub checkpoint/parked  | strip decoration, re-match classifier ⇒ owner derivable; metastream unwrap already exists in both assertion families |
| Cross-tenant aggregators | `$et-`, `$bc-`, `$streams`, unfiltered `$all`                     | no tenant derivable ⇒ admin-only for tenant principals (v1); per-tenant `$et-` = scope-explosion trigger, deferred   |
| Node infrastructure      | `$settings`, `$users`, `$projections-*`, `$scavenges`, `$tenants` | tenancy-invisible, admin-only (existing default posture)                                                             |

- Tenant `$all` = prefix SET: `{ "a7k3f9::", "$$a7k3f9::", "$ce-a7k3f9::" }`.
- Tenants self-service ACLs inside their namespace (assertion = hard floor, ACL = soft layer).

## C8. Aggregator gap → index-backed slices

- Gap is wire-deep: filter `oneof` ⇒ "my streams AND type X" has no native path.
- Options: **A** per-tenant `$et-` links (fast reads; projection surgery + per-event cost) ·
  **B** composite filters (scan cost — filters cut transmission, not IO) · **C** `$idx-*` with
  ambient tenant predicate + `tenant` column in `idx_all` (no new log records; verified
  machinery incl. `IndexSubscription`).
- Direction: **C**; A = escape hatch for proven-hot paths. Read menu: native prefixes (hot) →
  index slices (cross-cutting) → `$et-` links (escape hatch).
- Dependencies: SecondaryIndexing required for aggregate views; index lag ⇒ eventual consistency.
- Status: made LESS urgent by the minimum-work premise — app-controlled read scoping
  (Capacitor→Postgres, Kontext→DuckDB); `$idx-` injection deferred with the enforcement phase.

## C9. Category/prefix under the final form

- Prefix access needs no dash anywhere (filters, policies, `LIKE`).
- BOTH category mechanisms are first-dash-bound at single points: `$by_category`
  (`ProjectionManager.cs:894,901`) and index `GetStreamCategory`
  (`DefaultIndexProcessor.cs:128-131`).
- Options: (A) dash-at-leaf — forbids kebab types · (B) prefix-only — loses fast `category=$1`
  slices · **(C) RECOMMENDED: namespace-aware derivation** — classifier match ⇒ category =
  parent path (`a7k3f9::platform/project`); all other names keep first-dash byte-for-byte. Two
  contained patches.

## C10. Reframe: namespace isolation over "pure" tenancy — VERIFIED (operator claim)

- **"Pure" tenancy needs core overhaul: CONFIRMED.** Evidence = this doc's own mitigation list:
  aggregator gap (wire-level) · log-global positions (side channel inherent to one log) · node
  singletons (no quota/noisy-neighbor boundary) · login = stream name · single replicated log.
  "Pure" = T2/T3.
- **Namespace isolation via conventions + claims + indexes: CONFIRMED** — every ingredient on a
  verified seam (C5). Bonus: groups surface as role claims ⇒ `$tenant:<key>` works in
  `StreamAcl` role arrays TODAY.
- Honest deltas: enforcement completeness burden · index views eventually consistent
  (leak-safe) · side channels remain ⇒ **cooperative** namespaces, not adversarial tenancy —
  the product positioning line.
- Consequence: multi-claim membership becomes plausible (claim set + multi-prefix machinery) —
  diverges from Kontext's exactly-one rule; to be decided.

## C11. Capacitor alignment (source + tracker, verified 2026-08-01)

- Source (`origin/main`): tenant = AuthProxy record + env-only plan class; **workspace absent**
  (only `FlowWorkspace` scratch + Slack strings; GitHub Teams proxied read-only); project =
  full ES aggregate. Hierarchy tenants → workspaces → projects is TARGET state.
- Read/write shape: one `KurrentDBClient`; Eventuous `KurrentDBEventStore`; ~12
  `AllStreamSubscription`s (unfiltered `$all`) → Postgres/pgvector read models; all queries hit
  Postgres; single append funnel with `BuildMetadata()` (`SessionWriter.cs:107-116,347-359`).
- Metadata keys today: `$ingester_user_id` (+historical `$ingester_github_id`), `$vendor`,
  `$source`, `$acpSeq`, `$lineNumber`, `$usage`. No tenancy keys; no correlation stamping.
- Linear roadmap (AI-1558 "shared free cells", Urgent, In Progress; PR #1214):

| Issue                                                                                                         | Phase           | Status                         |
|---------------------------------------------------------------------------------------------------------------|-----------------|--------------------------------|
| AI-1574 team-tenancy foundation (workspace teams, org↔team registry, fail-closed claims gate, `ITeamContext`) | identity        | **Done 2026-08-01** (PR #1259) |
| AI-1575 `team_id` data partition (metadata stamping, read-model columns, mandatory query wrap, leak tests)    | data            | Backlog, unblocked             |
| AI-1576 cell deployment + signup · AI-1577 cross-team closure · AI-1578 data movement                         | infra/hardening | Backlog                        |

- Convergence: Capacitor hand-rolls the same patterns one layer up (ambient fail-closed
  predicate, claims gate, leak tests) because the engine has no primitive. **Capacitor =
  consumer zero**; under namespaces, AI-1575's `team_id` stamping becomes redundant (name
  carries workspace) and team-qualified repo identity falls out free. Sequencing belongs in
  both AI-1575's spec and this PRD.
- Kontext cross-feature note: lanes = `tenant`/`workspace` promoted isolation columns +
  `project`/`repo` scoping tags; amends the MCP-auth one-workspace-per-user decision — flagged
  in that design doc, not yet applied.
