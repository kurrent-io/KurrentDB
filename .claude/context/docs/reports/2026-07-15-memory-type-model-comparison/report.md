---
title: MemoryType Taxonomy — Fable/Sonnet/Haiku Example-Generation Comparison
type: analysis
date: 2026-07-15
author: sergio
tags: [kontext, memory-type, taxonomy, model-comparison, evaluation]
scope: Kontext v3 flat MemoryType (OBSERVATION/HEARSAY/FACT/PROCEDURE/PROFILE/PLAN/SUMMARY)
---

## Summary

Three agents — one **Fable**, one **Sonnet**, one **Haiku** — were each given the *same* fixed,
non-Kontext context (a small coffee shop, "The Nook") and asked to produce **3 example memories for
each of the 7 flat `MemoryType` values**. Purpose: sanity-check that the new taxonomy is *legible* —
i.e. that models can sort real-world material into these seven types with little confusion.

**Verdict: the taxonomy is legible.** All three models — including the smallest — sorted material
into the seven types correctly and with near-identical intent. The only fuzzy spots were the
taxonomy's *genuine* semantic boundaries, not model failures:
- **OBSERVATION ↔ SUMMARY** — aggregate stats ("weekly sales up ~10%") landed in OBSERVATION for
  Sonnet and Haiku, but it's really a derived SUMMARY. Fable kept OBSERVATION to raw perceived events.
- **PROCEDURE ↔ PROFILE** — "keep oat milk stocked" / "playlist low after 4pm" appeared as both a
  house *convention* (PROCEDURE) and Priya's *preference* (PROFILE). Both readings are defensible.
- **FACT ↔ PROFILE** — "Priya is the owner" appeared in both; owner-identity is a durable fact *and*
  a profile fact.

That the models' only disagreements cluster on exactly three boundaries — and no model produced
nonsense — is a positive signal for the design.

## Findings

### Metrics

| Model  | Wall-clock | Output tokens | Notes |
|--------|-----------:|--------------:|-------|
| **Fable**  | 12.9 s | 27.4k | Cleanest OBSERVATIONs (all raw events); warm phrasing; HEARSAY least diverse (3 variants of Tom's one claim). |
| **Sonnet** | **7.5 s** | 32.6k | Fastest; most tokens; strong across the board; slight OBSERVATION↔SUMMARY blur. |
| **Haiku**  | 60.5 s | **18.6k** | Most economical tokens; most diverse HEARSAY; tightest formatting. Slowest wall-clock — see caveat. |

**Effort:** not independently set per agent — the Agent tool exposes a model override but not an
effort override, so all three ran at the session's inherited effort. Per-agent effort control would
require the Workflow harness. Effort was therefore constant across the three (a fair basis for a
model-only comparison).

**Timing caveat:** wall-clock includes scheduling/queueing, not just model latency. Haiku being the
*slowest* (60 s) despite being the smallest model is counterintuitive and almost certainly
contention/cold-start, not capability — do not read it as a model-speed benchmark.

### Per-type read on quality

- **OBSERVATION** — all good on raw events (sputtering steam wand, Thursday slow). Sonnet/Haiku also
  filed "sales up ~10%" here, which is really a SUMMARY (the one consistent mislabel).
- **HEARSAY** — all correctly attributed unverified claims to Tom/customers. Haiku & Sonnet invented
  plausible extra hearsay (machine "might need servicing", shop "felt quieter"); Fable stayed on
  Tom's price claim (less diverse but safest).
- **FACT** — all correct (opens 7:00, competing café exists, owner Priya).
- **PROCEDURE** — all correct (closing routine; oat-milk/​playlist as conventions).
- **PROFILE** — all correct (Priya's identity + standing preferences).
- **PLAN** — all correct, and all three sensibly *inferred* future actions beyond the stated supplier
  trial (service the steam wand; review the Thursday dip).
- **SUMMARY** — all produced good period consolidations (sales trend + week's events digest).

### Full outputs

**FABLE**
```
OBSERVATION:
- This morning the espresso machine's steam wand was sputtering during service.
- Tom, a regular, came in and chatted at the counter this morning.
- Thursday's sales were unusually slow compared to the rest of the week.
HEARSAY:
- Tom said the competing café down the street just raised its prices.
- Tom implied the competitor's price hike is pushing some of their customers away.
- According to Tom, the down-the-street café's regulars are grumbling about the new prices.
FACT:
- The Nook opens at 7:00 every weekday.
- The Nook is a small independent coffee shop owned by Priya.
- There is a competing café down the street from The Nook.
PROCEDURE:
- The closing routine is: wipe the machine, count the till, take out the recycling, then set the alarm.
- The alarm is always set as the final step of closing, after the recycling goes out.
- Oat milk stock should be checked and replenished before it runs out, per standing convention.
PROFILE:
- Priya is the owner of The Nook.
- Priya always wants oat milk kept in stock.
- Priya dislikes the playlist getting loud after 4pm.
PLAN:
- Trial the new pastry supplier next Monday as Priya requested.
- Get the espresso machine's sputtering steam wand looked at or repaired soon.
- Confirm oat milk is on the next stock order before the trial-supplier Monday.
SUMMARY:
- Sales this week were up about 10% overall, with Thursday as an unusually slow outlier.
- This week's notable events: a sputtering steam wand, a competitor price rise reported by Tom, and a planned pastry supplier trial.
- Recent equipment and stock themes: the espresso machine needs attention while day-to-day supplies like oat milk have stayed on track.
```

**SONNET**
```
OBSERVATION:
- This morning the espresso machine's steam wand was sputtering during use.
- Sales this week were up roughly 10% compared to the prior week.
- Thursday's sales were unusually slow compared to the rest of the week.
HEARSAY:
- Tom said the competing café down the street just raised its prices.
- A regular reported that the café up the block is charging more now.
- Unconfirmed customer chatter suggests the price hike at the nearby café may be significant, per Tom.
FACT:
- The Nook is a small independent coffee shop.
- The shop opens at 7:00 every weekday.
- Priya is the owner of The Nook.
PROCEDURE:
- Closing routine: wipe the machine, count the till, take out the recycling, then set the alarm.
- Keep oat milk stocked at all times per standing store policy.
- Keep the playlist volume down after 4pm as house convention.
PROFILE:
- Priya, the owner, always wants oat milk kept in stock.
- Priya dislikes the playlist getting loud after 4pm.
- Priya is currently interested in evaluating new pastry suppliers.
PLAN:
- Priya wants to trial a new pastry supplier next Monday.
- Get the steam wand issue looked at or serviced before it worsens.
- Keep an eye on Thursday sales trends to see if the slowdown repeats next week.
SUMMARY:
- This week's overall sales were up ~10%, with an anomalous dip on Thursday.
- Recent shop activity included an equipment hiccup (steam wand), a competitor pricing rumor from a regular, and an upcoming supplier trial planned by the owner.
- Owner preferences observed this period: consistent oat milk stock and quieter afternoon music after 4pm.
```

**HAIKU**
```
OBSERVATION:
- The espresso machine's steam wand was sputtering this morning
- Weekly sales increased by approximately 10%
- Thursday's sales were notably slower than other days this week
HEARSAY:
- Tom mentioned the competing café down the street raised its prices
- A customer suggested the espresso machine might need professional servicing soon
- A regular claimed the shop felt quieter than usual on Thursday afternoon
FACT:
- The Nook opens at 7:00 AM every weekday
- A competing café is located down the street
- Priya is the owner of The Nook
PROCEDURE:
- Closing routine: wipe the espresso machine, count the till, take out the recycling, then set the alarm
- Keep oat milk stocked at all times
- Keep the playlist volume low after 4pm
PROFILE:
- Priya is the owner of The Nook
- Priya wants oat milk kept in stock
- Priya dislikes loud playlist volume after 4pm
PLAN:
- Trial a new pastry supplier next Monday
- Investigate the espresso machine steam wand malfunction
- Review Thursday's sales dip to understand the cause
SUMMARY:
- Weekly sales up ~10% overall, though Thursday was an unusual low point
- This week's equipment status: espresso machine steam wand showing signs of wear
- Competitive intelligence: nearby café has raised prices recently
```

## Recommendations

- **Ship the taxonomy as-is.** Legibility is confirmed across model tiers.
- **Document the three known boundaries** in the enum/tool docs so agents route consistently:
  aggregate stats → SUMMARY (not OBSERVATION); a *convention* → PROCEDURE vs a person's *preference*
  → PROFILE; owner/role identity → PROFILE (with FACT reserved for world/business truths).
- No taxonomy change indicated by this run.

## Method

- **Context:** fixed, shared, non-Kontext (coffee shop "The Nook") to avoid inception bias.
- **Task:** identical prompt to all three — the 7 type definitions + the context + "3 examples per
  type, one sentence each, grouped by type."
- **Models:** Fable / Sonnet / Haiku via the Agent tool's `model` override. Effort inherited
  (constant); not independently settable per agent (Workflow-only).
- **Metrics:** wall-clock and output tokens from each agent's completion usage. Wall-clock includes
  scheduling/queueing — not a clean latency benchmark (see caveat).
- **Not covered:** multiple runs / variance (single run each); scoring against a gold labelling
  (assessed qualitatively).
</content>
</invoke>
