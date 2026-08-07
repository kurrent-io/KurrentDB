---
name: root-directory-build-props-is-live
description: Root Directory.Build.props IS loaded by a mechanism outside the in-repo MSBuild chain — never flag it as dead config or propose reverting it
metadata: 
  node_type: memory
  type: feedback
  originSessionId: ce9d4cbe-08e8-40fd-aec7-f1957b029319
  modified: 2026-08-05T11:16:16.762Z
---

The repo-root `Directory.Build.props` (TargetFramework, LangVersion preview, Nullable, etc.) is IN USE.
Sérgio confirmed on 2026-08-05 that it is loaded by a mechanism not discoverable from the repo's MSBuild
import chain (nearest-wins analysis says no `src/` project reads it — that analysis is incomplete here).

**Why:** On 2026-08-05 I classified the file's property block as dead/duplicated/conflicting against
`src/Directory.Build.props` and proposed reverting it to master's minimal form. Sérgio rejected this:
"It is being loaded. You just don't know how. It is how it is. Leave it alone."

**How to apply:** Treat the file as live configuration. Never propose deleting, reverting, or "cleaning"
it. If something inside it is actually broken, fix it in place — because it is in use. The apparent
LangVersion divergence (root `preview` vs src `14.0`) is intentional layering, not a conflict to resolve.
