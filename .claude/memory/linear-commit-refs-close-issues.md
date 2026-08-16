---
name: linear-commit-refs-close-issues
description: Linear automation in this workspace closes issues referenced from pushed commits — reference only the issue a commit actually completes
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d2c6fd63-c002-48d7-bc13-c87c234a1875
  modified: 2026-08-16T16:54:37.748Z
---

2026-08-16: a commit body carrying `Refs DEV-1875, DEV-1876` auto-moved BOTH issues to Done on
push — including DEV-1876, whose work had not started. In this workspace, Linear's commit-link
automation completes referenced issues regardless of the `Refs` (non-closing) keyword.

**Why:** a false Done on an open issue is a record-integrity failure, and the workflow contract
forbids me from setting issue state to repair it.

**How to apply:** from a commit or PR, reference ONLY the issue that change completes. Related
open issues get linked from the Linear side (relations, comments), never from a commit body.
