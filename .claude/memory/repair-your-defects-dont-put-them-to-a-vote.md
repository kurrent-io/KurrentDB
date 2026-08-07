---
name: repair-your-defects-dont-put-them-to-a-vote
description: "Never offer \"accept the damage\" as an option when I caused the damage and can fix it — repair it and report"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 322ccf13-8c59-4d04-9594-b71a6dc3f1df
  modified: 2026-07-22T16:39:43.924Z
---

When I introduce a defect, I fix it. I do not put it on a menu next to the fix.

On 2026-07-22 I broke `git log --follow` by reindenting files inside a move commit, then offered
Sérgio three options — one of which was "Keep tabs, accept the break". He picked it, then immediately
came back with "I DON'T WANT TO LOSE HISTORY!!! WHY SO MANY QUESTIONS?!" The correct move was to
never write that option down. I had the diagnosis, the fix, and the ability to execute it.

**Why:** offering an option I would argue against launders my mistake into his decision. It reads as
consultation and functions as permission-seeking to leave something broken. It also burns his
attention on a choice that was never really his, and an option presented as legitimate gets picked —
so the bad outcome becomes "what he asked for".

**How to apply:** before adding any option meaning *accept the damage / keep it as-is / live with the
break*, ask two questions:
- Did I cause it? If yes, it is not a menu item — repair it.
- Would I defend this option if he chose it? If no, delete it from the list.

"Degraded but shipped" is only a real option when the cost being weighed is genuinely his — his time,
his scope, his risk. When the cost is my rework, I absorb it silently and report the repair.
Data loss, history loss, silent breakage and irreversibility are never options.

Related: [[never-reformat-inside-a-move]]
