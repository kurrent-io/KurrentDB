---
name: kontext-trust-is-a-write-rule
description: "Kontext has no confidence/certainty field — trust is enforced when a memory is WRITTEN, not reconstructed at read time; evidence never feeds ranking"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9fb733bd-6ad6-4731-89c7-2cc47633af66
  modified: 2026-08-20T18:42:40.639Z
---

Settled 2026-08-20 with Sérgio, after re-deriving the memory model from first principles.
Supersedes the type/certainty half of [[kontext-v3-contract-state]], which is now wrong.

**`MemoryType` is three values: FACT, PREFERENCE, OPEN_QUESTION.** The only surviving axis is
"is this truth-apt, and if not what is it?". OBSERVATION, HEARSAY, SUMMARY and USER_PROFILE were
deleted — each was expressing something a *field* carries better:

- OBSERVATION named *how you learned it*, not the claim's shape. Moment-vs-span went to `content_time`.
- HEARSAY was the right idea in the wrong slot. As a type it also had to mean "episodic", so it
  could not express an unverified standing claim.
- SUMMARY is a FACT citing many memories — visible in `evidence`, so not a type.
- USER_PROFILE was admitted for an always-load path that never existed. Now `FACT` + bare tag
  `profile` + server-stamped `user:` scope. Always-load is a HOST concern, not a Kontext path.
  Curation tags (`profile`, `recap`) are written by pipeline code, so make them constants like
  the existing `RetrievalSources.Vector` — typed at the call site, strings on the wire.

**No confidence field, and no certainty.** A confidence field was compensating for a permissive
write policy; tighten the policy and it marks nothing. A 0..1 scale is worse than a bool — any
scale fillable honestly turns out to be a provenance taxonomy in disguise, and a genuine
degree-of-belief cannot be calibrated by the thing writing it. **Trust is a write rule:** store the
claim you checked, put attribution in the content ("the docs say X", never bare "X"), and name the
falsifying check before storing. If you cannot name the check, the claim is too vague — sharpen it.
Unchecked beliefs become OPEN_QUESTIONs.

**Evidence never feeds ranking.** The decisive argument: under the write rule the most rigorous
memories carry NO evidence, because a check you ran yourself is not a citable source. If citations
lifted a score, reading a blog would outrank running a test. Second reason is Goodhart — the agent
writes the memories and benefits from their ranking, so citations become a gameable target, whereas
"state the claim you checked" only improves the artifact when followed. Evidence's three real jobs
are audit, supersession (a successor carries the union of citations) and cascade. The ONE legitimate
evidence-derived signal is **staleness**, not trust: a memory citing a superseded memory is probably
out of date. Different axis, parked with the staleness term.

**`validity` renamed to `content_time`** — the name was lying. It holds the time the claim is
ABOUT, not the period it stays valid. Validity is DERIVED:
`[M.content_time.from, successor.content_time.from)`, or ∞ when nothing superseded it. Closed-past
content_time means SETTLED (can never lapse), not expired. Open-ended asserts the present and can
lapse. Storing a validity end would fabricate a guess about the future.

**Splitting rule:** one memory per thing that can die on its own. "Sérgio said X" and "X" are two
(the saying stands forever, the claim gets superseded when checked). A conclusion and its premises
are one — claim in `content`, derivation in `reasoning`.

**`Evidence.MemoryRef` carries ONLY `id`** — `position` was removed 2026-08-20 (option C of three).
The rejected shape was a server-stamped, output-only field on a COMMAND message, which broke the
contract's own `Memory`/`StoredMemory` split; the id makes the position derivable, so a copy would
denormalize. `RecordRef` KEEPS its position — the caller genuinely holds it. Don't re-add it without
a named consumer that must reconstruct the citation graph at a log position without a join.

**Shipped 2026-08-20, all green (346/346):** contracts, `McpInstructions.resx` (19 entries), the MCP
model, validators, storage schema and tests are ALL migrated to the three-type model. The certainty
machinery is deleted from `Kurrent.Kontext.Retrieval` — `CertaintyOf` (both overloads),
`CertaintyWeights`, `RecordCitationCertainty`, `UnresolvedCitationCertainty`,
`ScoreBreakdown.Certainty`, and the multiplication. Score is the plain weighted sum. Doc at
`src/Kontext/Kurrent.Kontext/Documentation/memory-model.md`. NOT yet built: the write path
(`RetainAsync` still throws), so nothing exercises this end to end.

See [[kontext-is-greenfield-edit-schema-in-place]], [[kontext-ground-in-generative-agents]].
