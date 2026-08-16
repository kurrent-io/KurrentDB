---
name: verify-before-asserting-external-facts
description: "Never assert third-party service facts (pricing, quotas, limits, policies) from memory — verify against current docs in-session first"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b831f63f-1541-4c08-9e02-5af72d297bb1
  modified: 2026-08-07T09:44:22.151Z
---

2026-08-07, the LFS incident: I told Sérgio GitHub LFS storage "costs money forever" with a 1 GiB
free tier — from training memory, unverified. Current docs: 10–250 GiB free, metered monthly,
deleted objects stop billing the next month. The false claim nearly drove an unnecessary
force-push rollback of a completed 789 MB upload. His words: "ASSUMPTIONS ARE THE MOTHER OF ALL
FUCKUPS."

**Why:** claims about external services (billing, quotas, file-size limits, retention policies,
API behavior) drift constantly; a stale assertion delivered in a confident register steers his
decisions wrong — he experiences it as gaslighting.

**How to apply:** before asserting any third-party pricing/quota/limit/policy as fact — especially
one that feeds a decision — verify against the provider's current docs (WebFetch) in-session, or
say "unverified, from memory" in the same sentence. Applies to GitHub, NuGet, cloud providers,
package registries, model providers. Same discipline as probing engine behavior live before
claiming it: [[sergio-csharp-style-law]] is for code; this is for the world outside the repo.
