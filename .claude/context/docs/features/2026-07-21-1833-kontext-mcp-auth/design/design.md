---
title: Kontext MCP Authentication
status: settling
authors: [sergio]
date: 2026-07-21
tags: [kontext, mcp, oauth, oidc, openiddict, identity, workspace]
---

# Design Space — Kontext MCP Authentication

> Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
> **append-leaning**. This doc is also the feature's decision record — the rejected options and the
> "why not" are the value.

## Problem / Trigger

The Kontext MCP edge needs real authentication. The prototype (`KurrentDB.Plugins.Kontext`) binds
sessions at `/kontext/mcp/{workspace}` with KurrentDB basic auth: a human courier hands each user a
magic URL with the workspace baked in plus a shared credential for `.mcp.json`. That collapses with
more than one user, leaks like any shared secret, and puts a *resource name* in the position of an
*identity claim*. Meanwhile the agent itself can never supply identity (it hallucinates and can't be
trusted with tenancy — see the identity-scoping research), so identity must come from the layers
around the model: the official MCP OAuth flow.

Wanted: spec-native MCP authentication — one stable identity-free URL, browser login, tokens the
node can validate — plus **login and self-registration pages** so the self-hosted/local scenario
works without an admin provisioning users.

## Exploration

### Prior research (both filed, both feed this design)

- `.claude/context/docs/research/2026-07-21-1716-memory-identity-scoping/research.md` — how every
  serious memory system + the MCP spec handle identity: never model-supplied; bound at the app,
  transport, or environment layer. The spec's normative line: *"the user ID is derived from the user
  token and not provided by the client."*
- `.claude/context/docs/research/2026-07-21-1759-embeddable-idp-for-mcp/research.md` — the IdP
  landscape: ASP.NET Core has no first-party authorization server (verified against .NET 10 docs);
  Duende IdentityServer is technically the best MCP fit but its redistribution licensing disqualifies
  embedding in a commercial product; OpenIddict (Apache 2.0) is the only credible embeddable option;
  the MCP spec draft deprecates DCR in favor of CIMD.

### The flow (what the MCP spec standardizes vs what is ours)

```
MCP client → /kontext/mcp → 401 + WWW-Authenticate
  → RFC 9728 protected resource metadata            [MCP C# SDK AddMcp() — built in]
  → RFC 8414 AS metadata discovery                  [OpenIddict — built in]
  → client registration: CIMD (or DCR fallback)     [our handlers]
  → browser opens /connect/authorize (PKCE, resource=…)  [OpenIddict — built in]
      → Challenge(cookie) → login page              [OURS — spec deliberately silent]
      → (register link, when enabled)               [OURS — spec deliberately silent]
  → code → /connect/token → JWT                     [OpenIddict — built in]
  → Bearer on every MCP request → validated in-node [exists: KurrentDB.Auth.OAuth pattern]
```

The login/register screens are NOT a deviation from "official MCP mechanisms" — OAuth/MCP
standardize everything up to the browser opening the authorization endpoint; what the human sees in
that tab is the AS's own UI in every OAuth deployment (GitHub, Google, Auth0 all bespoke).
OpenIddict ships **zero** screens by design; the only prebuilt ones in .NET are ASP.NET Identity's
scaffold, which drags in its own user store. We bring two small pages over our own `$user-` store.

### Client registration reality (mid-2026)

| Client | Mechanism |
|---|---|
| Claude Code ≥ 2.1.81 | CIMD preferred (SEP-991); DCR fallback only if AS metadata lacks CIMD flags |
| VS Code | Both (reference implementation) |
| Cursor | DCR-only (as of Jan 2026 forum response) |

⇒ ship CIMD (cheap: fetch + validate the metadata JSON at the URL-shaped `client_id`, stateless)
**and** a minimal anonymous DCR endpoint for Cursor.

### Identity model (settled through the session)

- **`user`** = KurrentDB `LoginName` → JWT `sub`. Server-derived, never a tool arg, never a URL.
- **`workspace`** = TENANT. **Exactly one per user.** Stored as a group with the system-style prefix
  convention `$kontext-workspace:<name>` in the existing `UserData.Groups` (`$user-<login>` streams
  — `src/KurrentDB.Core/Data/UserData.cs`). Managed with the existing users API/UI; no new storage.
- Internal-auth path: groups already surface as `ClaimTypes.Role` claims
  (`InternalAuthenticationProvider.cs` → `CreatePrincipal`). An `IClaimsTransformation` promotes the
  prefixed group to a single **`kontext_workspace`** claim once at authentication; everything
  downstream reads a plain claim — no prefix-parsing helpers at call sites.
- OAuth path: the IdP mints `kontext_workspace` directly into the access token (single string, not
  array). Plain groups go to `role`.
- **Fail closed**: zero or multiple `$kontext-workspace:` groups ⇒ no claim ⇒ no Kontext access.
- Staleness: claims are snapshots at issuance; group changes land at next refresh. Short access-token
  lifetimes bound the window.
- `project` stays an advisory dimension (config / MCP roots / agent tag — see scoping research);
  it is NOT part of this auth design.

Token shape:

```json
{
  "iss": "https://node:2113/kontext",
  "sub": "sergio",
  "aud": "https://node:2113/kontext/mcp",
  "role": ["$admins"],
  "kontext_workspace": "default",
  "exp": 1753112400
}
```

### Components (all in-process, no external IdP, no container)

1. **AS: OpenIddict `OpenIddict.Server.AspNetCore` in `EnableDegradedMode()`** — no database, no
   Core stores, no EF. Auth code + PKCE + refresh, discovery, JWKS all built in. Custom handlers we
   own:
   - `ValidateAuthorizationRequestContext` / `ValidateTokenRequestContext` — required by degraded
     mode; implement CIMD (URL client_id → fetch metadata → validate redirect_uri against declared
     list, public client) and validate the DCR-registered clients.
   - RFC 8707 handler — OpenIddict does NOT parse the literal `resource` parameter (it only maps
     scopes→resources); read it, validate it against the known MCP resource, `SetResources()` for
     the `aud`. This handler carries the audience-validation security burden (upstream #1440's fix
     status unverified) — test adversarially.
   - Claims handler — `sub` from LoginName, split groups into `role` vs `kontext_workspace`
     (strip prefix, enforce exactly-one), `SetDestinations(AccessToken)` per claim (OpenIddict
     copies nothing but `sub` by default).
2. **DCR endpooint (Cursor only)** — anonymous `POST /connect/register` per RFC 7591: validate
   redirect_uris against the RFC 8252 loopback shape (`http://127.0.0.1:<port>/…`) / custom schemes,
   mint a client_id, store in a small client registry. Registry v1 = in-memory (client re-registers
   after node restart; Duende's own MCP sample ships the same caveat); if persistence is wanted, a
   node system stream — NOT DuckDB (a handful of operational config rows is the wrong shape for the
   analytics engine).
3. **Resource side** — `ModelContextProtocol.AspNetCore`'s `AddMcp()` serves the RFC 9728 protected
   resource metadata and challenge; token validation delegated to JwtBearer against the embedded
   issuer's JWKS. Constraint from the SDK docs: authorization checks live in middleware/endpoint
   policy, NOT inside tool methods (the handler may flush response headers before a tool runs).
   `ActiveMcpSessions` keeps working — it stamps the (now token-derived) principal per session;
   the `{workspace}` route value is replaced by the `kontext_workspace` claim.
4. **Login page** — the node already runs cookie auth as its default scheme with
   `LoginPath = "/ui/login"` (`ClusterVNodeStartup.cs:185-228`). The authorize endpoint challenges
   the cookie scheme; credentials validate against the existing `$user-` hash/salt machinery.
5. **Register page** — new, anonymous-allowed, gated (below). Creates the user through
   `UserManagementService` acting as the system principal (the endpoint is the trusted actor).
   Server-fixed shape: `Groups = ["$kontext-workspace:default"]` — no group input from the form
   (mass-assignment guard); nobody self-registers into `$admins`. Registered users therefore always
   carry a workspace ⇒ fail-closed rule never bites the local user. Login page shows a
   "No account? Register" link only when the gate is on.
6. **`IClaimsTransformation`** (`KontextWorkspaceClaimsTransformation`) — promotes
   `$kontext-workspace:<ws>` role claims to the `kontext_workspace` claim for the internal-auth
   path; idempotent (skips when the claim already exists, which also covers the OAuth path).
7. **Signing key** — the ONE mandatory persisted artifact. RSA key on disk in the node's data/cert
   area; lose it and every outstanding token dies + JWKS changes. Tokens themselves: never stored —
   validity is cryptographic (signature + exp + aud + iss), restarts are invisible to clients.

### Configuration

```
KurrentDB:Kontext:Enabled                  (exists)
KurrentDB:Kontext:AllowSelfRegistration    NEW — default false; --dev flips to true
KurrentDB:Kontext:Auth:Issuer              default: node advertised HTTP address + /kontext
KurrentDB:Kontext:Auth:AccessTokenLifetime default: short (5–15 min; staleness/revocation window)
KurrentDB:Kontext:Auth:SigningKeyPath      default: under the node's existing key-material directory
```

Client side becomes one line, no credentials, no per-user URLs:

```json
{ "mcpServers": { "kontext": { "type": "http", "url": "https://node:2113/kontext/mcp" } } }
```

### Trade-offs accepted (v1)

- **No revocation**: stateless tokens mean a compromised token lives until `exp`. Mitigation: short
  access-token lifetime + refresh; revoking a *user* = disable/regroup them, which bites at next
  refresh (a user-store check, not a token store). Revisit storage the day revocation becomes a
  requirement.
- **No email verification / password reset / rate limiting on register**: defensible ONLY because
  the gate confines registration to local mode. Any future "self-registration on shared nodes" ask
  reopens this design; it must not be met by softening the gate.
- **DCR registrations lost on restart** (v1 in-memory): Cursor re-registers; Claude Code/VS Code
  unaffected (CIMD is stateless).

### Integration with the existing OAuth plugin (`KurrentDB.Auth.OAuth`) — added 2026-07-21

The embedded issuer and the existing plugin are not competitors — they serve the two deployment
modes, selected by the node's `AuthenticationType`:

| Node auth mode | Who issues tokens | Who validates `/kontext/mcp` requests | RFC 9728 metadata points at | Login/Register pages |
|---|---|---|---|---|
| `internal` (default) | **Embedded OpenIddict issuer** | Kontext-scoped JwtBearer against the embedded issuer's JWKS | the embedded issuer | active (register when gated on) |
| `oauth` (external IdP) | **The customer's IdP** — embedded issuer NOT started | the node's existing `OAuthAuthenticationProvider` already validates every HTTP request's Bearer JWT and populates `context.User` with the full token principal | the customer's IdP (same `Issuer` the plugin's Settings already hold) | inactive — users live in the IdP; self-registration meaningless |

Why this works with near-zero new integration code in `oauth` mode: the existing provider
(`OAuthAuthenticationPlugin.cs:241-267`) passes the **complete validated ClaimsPrincipal** through,
so `ActiveMcpSessions` captures IdP-minted claims as-is. Kontext's only jobs there are (a) serve
protected-resource metadata pointing at the external issuer — `AddMcp()` config read from the same
OAuth `AuthenticationConfig` section (one IdP config, two consumers), and (b) source the
`kontext_workspace` claim: either the corporate IdP mints it directly, or the
`IClaimsTransformation` maps it from whatever group/entitlement claim the IdP sends (mapping
configurable). Fail-closed still applies: no derivable workspace ⇒ no Kontext access.

Rejected sub-option — **self-referential `AuthenticationType=oauth` pointed at the embedded
issuer**: forces ALL node authentication (gRPC clients, ops tooling, UI) onto Bearer tokens
(`GetSupportedAuthenticationSchemes` is Bearer-only), and bootstraps badly — the provider's
`Initialize()` fetches the discovery document over HTTP and **throws fatally on failure**
(`OAuthAuthenticationPlugin.cs:128-177`), so the node would HTTP-call itself during startup before
its own endpoints are up. Rejected sub-option — **reusing `OAuthAuthenticationProvider` as a
Kontext-local validator**: its provider classes are private, and standard `AddJwtBearer` does the
same validation with less code; the plugin's value is node-pipeline integration, not its JWT code.

Unverified edge (check before building): how the HTTP layer maps `Authorization: Bearer` into the
`AuthenticationRequest.GetToken("jwt")` the provider reads — assumed working since the plugin is a
shipped feature, but the exact path wasn't traced this session. Likewise `RoleClaimType` mapping of
short `role` claims (one explicit line in `TokenValidationParameters` moots it).

### Token retention via DuckDB (optional extension) — added 2026-07-21

Requested addition: keep issued tokens "for a while" instead of fully stateless — buys **revocation**
(deny a live token, not just wait for `exp`), **refresh-reuse detection**, and **audit** ("what's
outstanding for this user/workspace"). Distinct from the client-registry storage question: a token
journal is append-heavy, TTL'd, per-node operational data — a defensible DuckDB shape (and the node
embeds DuckDB already), unlike the handful of client-config rows the earlier "no DuckDB" reasoning
was about. Two shapes:

- **Shape A — token journal, degraded mode kept (recommended first step).** OpenIddict stays
  storeless; our own event handlers append `(jti, subject, client_id, type, kontext_workspace,
  created_at, expires_at, status)` to a DuckDB table at issuance. Because issuer and resource server
  share the process, revocation checks are a cheap local lookup: JwtBearer `OnTokenValidated` (or
  the refresh-grant handler) rejects when the `jti` is marked revoked. Revoke = `UPDATE status`;
  background sweep deletes rows past `expires_at + retention window`
  (`Kontext:Auth:TokenRetention`, default ~token lifetime + grace). Small bounded code; the
  stateless-validity story is unchanged for nodes that never revoke.
- **Shape B — full `IOpenIddictTokenStore` over DuckDB.** Leave degraded mode; implement the
  OpenIddict token (and application) store interfaces (~25 methods, mechanical CRUD/queries) —
  unlocks reference tokens, the standard revocation endpoint, and native rotation/reuse detection.
  Only worth it if reference tokens or protocol-level revocation become requirements; also the point
  at which the DCR client registry would naturally live in the same DuckDB store.

## Decisions

- 2026-07-17 — (inherited from the v3 contract) Identity/scope are NOT contract fields; the
  principal is ambient from auth; caller-supplied identity is untrusted. See
  [[kontext-v3-contract-state]].
- 2026-07-21 — **Workspace-in-URL is dead.** Rejected the prototype's `/kontext/mcp/{workspace}`
  route: it makes a human courier distribute per-workspace URLs + shared credentials, and puts
  tenancy in a client-typable position. Endpoint becomes `/kontext/mcp`; workspace comes from the
  principal.
- 2026-07-21 — **One workspace per user; tenant isolation, not entitlements.** Rejected
  multi-workspace grants and the `GetKontextWorkspaces()` accessor style. Workspace is part of
  identity: `$kontext-workspace:<name>` group → single `kontext_workspace` claim via
  `IClaimsTransformation` / IdP claims handler. Fail closed on zero-or-many.
- 2026-07-21 — **Store workspace in `UserData.Groups`** with the `$`-prefixed convention. Rejected
  widening `UserData` (core-auth blast radius: events, API, UI, replication) and a separate
  Kontext user-attribute stream (not needed at single-value cardinality; groups are manageable with
  the existing users API today).
- 2026-07-21 — **OpenIddict `Server.AspNetCore` in degraded mode as the embedded AS.** Rejected:
  ASP.NET Core native (no first-party AS exists — verified against .NET 10 docs; `MapIdentityApi`
  tokens are opaque non-JWTs with no discovery); Duende IdentityServer (best technical fit, but
  embedding in a sold product is their textbook "redistribution" — excluded from the free edition,
  negotiated OEM pricing; needed features are Standard tier anyway — analysis is research-grade,
  confirm with counsel if ever revisited); SimpleIdServer (paywalls DCR + RFC 8707 — exactly what we
  need); Abblix (source-available proprietary, not OSS); external IdP container (Keycloak et al —
  violates "self-hosted node just starts"); hand-rolling the whole protocol (reinvents discovery/
  PKCE/JWKS that OpenIddict gives for free). Duende's McpDemo stays the architectural reference.
- 2026-07-21 — **No database for the AS. No DuckDB integration.** Degraded mode exists to have no
  stores; tokens are self-contained; claims are minted from the user record at issuance, not stored.
  Durable state = signing key (disk) + user records (`$user-` streams, existing) + optional DCR
  registry (in-memory v1; node stream if persisted — never DuckDB, wrong engine for operational
  config).
- 2026-07-21 — **CIMD + minimal DCR.** The MCP draft (SEP-991, Nov 2025) deprecates DCR for CIMD;
  Claude Code prefers CIMD, Cursor still needs DCR. Ship both; CIMD is the cheaper of the two
  (stateless handler, no registry).
- 2026-07-21 — **Self-registration behind `KurrentDB:Kontext:AllowSelfRegistration`** — explicit
  setting, default `false`, `--dev` flips it to `true`. Rejected inferring "local" from
  insecure/single-node (security posture by inference leaves doors open). Registered shape is
  server-fixed: `Groups = ["$kontext-workspace:default"]`.
- 2026-07-21 — **Login/consent UI is ours by necessity, and that IS the official mechanism.**
  OpenIddict ships no screens (verified — protocol only, by design); MCP/OAuth deliberately do not
  specify the AS's login UI. Two small pages over the existing `$user-` store; ASP.NET Identity's
  scaffolded UI rejected (drags in its own user store).
- 2026-07-21 — **Kontext auth rides the existing `KONTEXT` entitlement** — no separate license gate
  for the embedded issuer.
- 2026-07-21 — **AMENDMENT to "no database for the AS"**: a **DuckDB-backed token-retention
  integration** enters the design as a planned option (keep issued tokens for a while → revocation,
  refresh-reuse detection, audit). See the "Token retention via DuckDB" exploration for Shape A
  (journal, degraded mode kept — recommended first) vs Shape B (full `IOpenIddictTokenStore`).
  The no-store position stands for clients/scopes in v1; tokens are the carve-out.
- 2026-07-21 — **Coexistence with `KurrentDB.Auth.OAuth` is a mode switch, not an integration
  layer**: `internal` mode → embedded issuer + Kontext-scoped JwtBearer; `oauth` mode → embedded
  issuer not started, the existing provider's validated principal is used as-is, RFC 9728 metadata
  points at the external IdP (config read from the same OAuth settings section). Rejected:
  self-referential `AuthenticationType=oauth` against the embedded issuer (forces the whole node
  onto Bearer; fatal self-HTTP-call during `Initialize()` at boot), and reusing the plugin's
  private validator classes Kontext-locally (standard `AddJwtBearer` does the same with less).
  See the integration exploration subsection.

## Open Questions

- **Where do the pages live?** (2026-07-21: deliberately left open.) Reuse the Blazor UI area
  (`/ui/login` exists; add `/ui/register`) vs Kontext-owned minimal pages under `/kontext/`.
  Prerequisite either way: verify `/ui/login` honors an arbitrary `returnUrl` back to
  `/connect/authorize` (cookie wiring verified; the Blazor page's returnUrl handling is not).
- **Verify the two unverified edges of the mode-switch model** (see integration exploration):
  how the HTTP layer maps `Authorization: Bearer` to `AuthenticationRequest.GetToken("jwt")`, and
  `RoleClaimType` mapping of short `role` claims in the OAuth plugin's `TokenValidationParameters`.
- **Existing users without a workspace group** (e.g. `admin`): fail closed means no Kontext access
  until someone assigns `$kontext-workspace:<ws>`. 2026-07-21: not yet — to be discussed.
- **DCR registry persistence** (2026-07-21: undecided): in-memory (Cursor re-registers) vs node
  system stream vs — if Shape B token storage ever lands — the same DuckDB store.
- **Token retention specifics** (if/when the DuckDB extension is built): Shape A vs B, the retention
  window default, and where the revocation check sits on the validation path (JwtBearer
  `OnTokenValidated` vs refresh-grant-only).
- **External-IdP workspace mapping** (`oauth` mode): does the corporate IdP mint
  `kontext_workspace` directly, or does the claims transformation map it from a configured
  group/entitlement claim — and what does that config look like?

## References

**MCP protocol**
- Authorization (2025-06-18): https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization
- Authorization (current draft, SEP-991 — CIMD, DCR deprecated): https://modelcontextprotocol.io/specification/draft/basic/authorization
- Security best practices ("user ID derived from the token, not provided by the client"; sessions MUST NOT authenticate): https://modelcontextprotocol.io/specification/2025-06-18/basic/security_best_practices
- RFC 9728 (Protected Resource Metadata) · RFC 8414 (AS Metadata) · RFC 7591 (DCR) · RFC 8707 (Resource Indicators) · RFC 8252 (loopback redirects for native apps)
- Claude Code CIMD support: https://github.com/anthropics/claude-code/issues/18251
- Cursor DCR-only status: https://forum.cursor.com/t/mcp-oauth-cimd-support-plans-and-timelines/148096

**OpenIddict**
- Degraded mode (no-store operation): https://kevinchalet.com/2020/02/18/creating-an-openid-connect-server-proxy-with-openiddict-3-0-s-degraded-mode/
- Samples (Zirku/Mimban/Contruum — minimal authorize endpoints, own-user-store login): https://github.com/openiddict/openiddict-samples
- Claim destinations (nothing but `sub` copied by default): https://documentation.openiddict.com/configuration/claim-destinations.html
- DCR not shipped, tracked: https://github.com/openiddict/openiddict-core/issues/2404
- Audience validation rough edge (verify fix before relying): https://github.com/openiddict/openiddict-core/issues/1440
- 7.0 migration (scope→resource model, trim/AOT pass): https://documentation.openiddict.com/guides/migration/60-to-70

**MCP C# SDK / Microsoft**
- SDK v1.0 announcement (AddMcp, RFC 9728, middleware-not-tool-method constraint): https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/
- `ModelContextProtocol.AspNetCore.Authentication` API: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.AspNetCore.Authentication.html
- No first-party AS in ASP.NET Core (identity solutions matrix): https://learn.microsoft.com/aspnet/core/security/identity-management-solutions
- `MapIdentityApi` tokens intentionally non-JWT: https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization
- `dotnet user-jwts` (dev-time tokens): https://learn.microsoft.com/aspnet/core/security/authentication/jwt-authn

**Reference architecture**
- Duende McpDemo (anonymous DCR + MCP resource, maps ~1:1 onto OpenIddict primitives): https://github.com/DuendeSoftware/samples/tree/main/IdentityServer/v7/McpDemo
- Port-agnostic loopback redirects for MCP: https://duendesoftware.com/blog/20260707-port-agnostic-localhost-redirect-uris-mcp-auth

**Internal**
- Research: `.claude/context/docs/research/2026-07-21-1716-memory-identity-scoping/` · `.claude/context/docs/research/2026-07-21-1759-embeddable-idp-for-mcp/`
- Code: `src/KurrentDB.Plugins.Kontext/KontextPlugin.cs` (prototype binding) ·
  `src/KurrentDB.Core/Data/UserData.cs` · `src/KurrentDB.Core/Authentication/InternalAuthentication/InternalAuthenticationProvider.cs` (`CreatePrincipal`) ·
  `src/KurrentDB.Auth.OAuth/OAuthAuthenticationPlugin.cs` (validation half) ·
  `src/KurrentDB.Core/ClusterVNodeStartup.cs:185-228` (cookie scheme, `/ui/login`) ·
  `src/KurrentDB.Core/Services/Transport/Http/Controllers/UsersController.cs` (admin-gated user CRUD)
