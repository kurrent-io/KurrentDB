---
name: challenge-means-rederive
description: "When Sérgio questions a structure I wrote, re-derive from requirements — never marshal true-but-irrelevant facts into a defense of the current shape"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9a64c5a5-a252-41fd-89dd-e248ac6fb70c
  modified: 2026-08-15T15:45:27.277Z
---

When Sérgio challenges a structure ("what is the difference between X and Y?", "why do we have
this?"), the question is a design probe, not a request for advocacy. Re-derive the shape from the
requirements as if the code did not exist. Do NOT defend the artifact with facts that are true but
do not actually force the shape.

**Why:** 2026-08-15, GetIndexInfo/ReadVectorIndex. He asked the difference; I defended the split
with the one-connection consistency argument — the FACT was true, but the conclusion ("they cannot
merge") was false: the fact survived intact inside the merged single function. I also argued it in
stale pool-era vocabulary ("rented connections") after the pool was retired. He called it
gaslighting — technically-true justification of just-written slop reads as deception, and costs
more trust than "you are right, it collapses."

**How to apply:** on any structural challenge: (1) restate what the code must guarantee;
(2) construct the SIMPLEST shape that guarantees it, from scratch; (3) if that shape is simpler
than what exists, say so first and cut — the existing shape gets no vote for having been written.
Related: [[repair-your-defects-dont-put-them-to-a-vote]], [[sergio-csharp-style-law]].
