---
title: Embeddable IdP for the Kontext MCP edge — ASP.NET Core native vs OpenIddict vs Duende
type: research
date: 2026-07-21
author: sergio
tags: [kontext, mcp, oauth, oidc, identity, openiddict, duende]
---

## Question

KurrentDB needs the authorization-server half of the MCP OAuth flow (the resource-server half exists:
`KurrentDB.Auth.OAuth` validates Bearer JWTs). Is there a "super simple" way to implement OAuth in
ASP.NET Core, ideally with a small IdP embeddable in-process in the KurrentDB node — no external
container, no Keycloak dependency? Three parallel investigations: ASP.NET Core native capability,
OpenIddict (+ OSS survey), Duende IdentityServer.

## Findings

### The spec moved: DCR is deprecated in favor of CIMD

The current MCP authorization spec draft (SEP-991, ratified Nov 2025) makes **Client ID Metadata
Documents (CIMD)** the preferred client-registration mechanism; Dynamic Client Registration (RFC 7591)
is explicitly "deprecated and retained for backwards compatibility." Resource Indicators (RFC 8707)
remain a hard MUST in both spec versions. Client reality mid-2026:

| Client | Registration mechanism |
|---|---|
| Claude Code (≥2.1.81) | CIMD preferred; DCR fallback only if AS metadata lacks CIMD support flags |
| VS Code | Both CIMD and DCR (reference implementation) |
| Cursor | DCR-only as of Jan 2026 |

Covering all three today means supporting CIMD **and** a minimal DCR endpoint. CIMD is *less* work
than DCR: no persistent registration — fetch + validate the JSON metadata document at the URL-shaped
`client_id`, check `redirect_uri` against its declared list, treat the client as public.
Sources: modelcontextprotocol.io/specification/draft/basic/authorization ·
anthropics/claude-code#18251 · forum.cursor.com/t/mcp-oauth-cimd-support-plans-and-timelines/148096

### Option 1 — ASP.NET Core native: no authorization server exists, and .NET 10 didn't change that

- Verified against current Learn docs: ASP.NET Core has **zero first-party token-issuance
  capability**. The canonical "identity management solutions" page lists exactly two self-hosted
  library options: OpenIddict (Apache 2.0) and Duende (commercial).
- `MapIdentityApi` bearer tokens are a confirmed dead end: intentionally non-JWT (Data-Protection-
  encrypted, opaque), no discovery, no `/authorize`, no OAuth client registration, not validatable
  by an independent JwtBearer resource server. First-party same-origin SPA sessions only.
- `dotnet user-jwts`: legitimate **local-dev stopgap** — mints real JWTs validated by standard
  `AddJwtBearer` with zero code changes; no browser flow; works where the MCP client accepts a
  manually configured token.
- The official MCP C# SDK (`ModelContextProtocol.AspNetCore`) ships `AddMcp()` with **RFC 9728
  protected-resource-metadata** support — the discovery pointer that starts the client's OAuth dance
  is a first-party feature on the resource-server side, delegating validation to `AddJwtBearer`.
  Architectural constraint: authorization checks must live in middleware/endpoint policy, NOT inside
  MCP tool methods (the handler may flush response headers before a tool runs).
- Every Microsoft-authored MCP-auth example uses an external IdP (Entra ID). Self-hosted in-process
  means hand-rolling or a library.

### Option 2 — Duende IdentityServer: technically the best MCP fit, licensing kills it

- **Best-in-class MCP support**: first-party McpDemo sample with anonymous DCR
  (`MapDynamicClientRegistration()` with no auth policy — the exact MCP client pattern),
  `AddAppAuthRedirectUriValidator()` for any-port localhost redirects (built July 2026 specifically
  for MCP), RFC 8414 + RFC 8707, in-memory stores, custom `IProfileService` against our own user
  store. Minimal embedded host ≈ 6-8 files / 300-400 lines, no EF Core, no ASP.NET Identity.
- **Deal-breaker**: embedding IdentityServer in a product sold to third parties is Duende's textbook
  definition of "redistribution" — explicitly excluded from the free Community Edition, requiring a
  scale-priced negotiated Redistribution License with per-customer license-key shipping. Even
  ignoring OEM: DCR and Resource Indicators sit in the **Standard tier ($12,500/yr)**, not Lite.
- Specialist verdict: IdentityServer saves ~200 lines of DCR code and costs an OEM contract attached
  to every KurrentDB sale. Use OpenIddict; keep the McpDemo as the reference architecture (maps
  ~1:1 onto OpenIddict primitives).
- Label: pricing/licensing analysis is research-grade, from duendesoftware.com/pricing and the
  Community Edition page as of 2026-07-21 — have counsel confirm before any decision that touches it.

### Option 3 — OpenIddict: the only credible embeddable OSS option

- Apache 2.0, actively maintained (7.6.0 shipped 2026-07-15; releases every 1-2 months; single lead
  maintainer, Kévin Chalet).
- **`EnableDegradedMode()` = genuine no-database embedding**: disables authorization/token storage
  and built-in client validation; you supply `ValidateAuthorizationRequestContext` /
  `ValidateTokenRequestContext` handlers instead. Estimated ~150-220 lines for a minimal
  auth-code+PKCE server bound to our own user store (inferred by subtracting EF/Quartz from the
  ~340-line Zirku sample; no official minimal degraded-mode sample exists).
- **Login UI**: deliberately not provided — a single login page/minimal-API form bound to the
  KurrentDB internal user store is the intended integration; no ASP.NET Identity anywhere.
- **Gaps, all bounded custom-handler work**:
  - DCR: not shipped (openiddict-core#2404, milestone 8.0.0-preview.3). Hand-roll `/connect/register`
    over `IOpenIddictApplicationManager.CreateAsync` (~100-200 lines) — needed for Cursor only.
  - CIMD: not on the roadmap; hand-rolled validation handler — less work than DCR, no storage.
  - RFC 8707: OpenIddict does NOT parse the literal `resource` parameter — it maps scopes→resources
    via `SetResources()`. Custom handler must read the `resource` param, validate it, set audience.
    Related rough edge: #1440 (audience validation at authorize time) — fix status unverified;
    re-check the dev branch before relying on it.
  - Custom claims: opt-in per claim via `SetDestinations(Destinations.AccessToken)` (by design).
  - JWKS/discovery: built-in; access tokens are `at+jwt` (RFC 9068 style).
- AOT: 7.0 did a trim/AOT pass; EF Core stores still rough — degraded mode avoids exactly that part.
- **OSS survey**: nothing displaces it. SimpleIdServer paywalls DCR (Business) and RFC 8707
  (Enterprise) — the two features we need. Abblix is source-available proprietary, not OSS.
  OrchardCore/pixel-identity wrap OpenIddict. Hydra/Dex/Keycloak are containers, out of scope.

## Implications

**Recommendation: embed OpenIddict in degraded mode for the hosted/team story; keep Duende's McpDemo
as the architectural reference; `dotnet user-jwts` or internal/basic auth for local dev.**

Build shape for the Kontext MCP edge:

1. Resource side (mostly exists): `KurrentDB.Auth.OAuth` validation + `AddMcp()` protected-resource
   metadata from the MCP C# SDK, pointing at the embedded issuer.
2. AS side (new, in-process): OpenIddict degraded mode + one login page over the internal user store
   + CIMD handler + minimal DCR endpoint (Cursor) + `resource`-parameter handler (RFC 8707 audience)
   + JWKS consumed by the validation plugin. Realistic total ≈ 600-900 lines — bounded, no external
   dependency, no licensing machinery.
3. Open design decision (not solved here): the node's `AuthenticationType` is global and singular —
   an embedded issuer + the node's own OAuth validation pointing at itself is self-referential but
   workable; alternatively the Kontext endpoints run their own auth scheme. Needs its own design pass.

Risks: OpenIddict is effectively a one-maintainer project (mitigated by Apache 2.0 + forkability);
CIMD support is hand-rolled against a spec still in draft; the RFC 8707 handler carries the security
burden of audience validation (test adversarially); Duende licensing reading needs legal confirmation
if ever revisited.

Related: [[2026-07-21-1716-memory-identity-scoping]] (who supplies user/project/workspace — the
precedence chains this IdP work slots into).
