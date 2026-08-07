---
name: no-breadcrumb-comments
description: "Never leave \"moved to X\" / migration-trail breadcrumb comments in code"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1a34a86a-f87c-4d88-87f9-fd483f202c3c
---

Sergio (2026-07-10): never leave breadcrumb comments — e.g. `// X moved to resources.proto`, `// was session_id`, `// see other file` — after moving, removing, or renaming code. It reads as unprofessional.

**Why:** the code and git history already record what changed; a pointer comment is clutter that goes stale and signals an incomplete edit.

**How to apply:** when relocating/removing/renaming a type, field, or function, just make the change cleanly and leave nothing behind at the old site. If a genuine explanation is needed, it belongs on the code as it now stands (the *why* of the current design), not as a note about what used to be there or where it went. Related: [[no-unauthorized-scope-cuts]].
