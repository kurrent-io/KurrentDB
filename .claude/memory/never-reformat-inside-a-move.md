---
name: never-reformat-inside-a-move
description: Never reindent/reformat a file in the same commit that moves it — it destroys git rename detection and breaks log --follow
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 322ccf13-8c59-4d04-9594-b71a6dc3f1df
  modified: 2026-07-22T16:28:12.029Z
---

When moving files, the move commit must change as little content as possible. Reformatting
(reindenting, reflowing, mass-renaming) inside a move commit destroys `git log --follow`.

Git does not store renames — it infers them at diff time from content similarity, default threshold
50%. A reindent touches every line, so a moved-and-reformatted file scores as low as 11% and commits
as delete + create, which reads as "history deleted".

**Why:** Sérgio reacted strongly ("I'm SCARED", "I DON'T WANT TO LOSE HISTORY") on 2026-07-22 when
`src/KurrentDB.Core/Hosting/` showed no history after I moved files there and converted them from
4 spaces to tabs in one step. History preservation is not negotiable for him — do not offer
"accept the break" as an option.

**How to apply:** split into two commits.
1. `git mv` + only the edits required to compile (namespace lines, usings, visibility). Keep the
   ORIGINAL indentation, even if it violates `.editorconfig`. Verify with
   `git diff --cached -M --summary` — every moved file must appear as `rename ... (NN%)`, not `create`.
2. Formatting pass on its own. Verify with `git diff -w HEAD~1 HEAD` — must be empty.

Check which indentation the ORIGINAL used before "conforming" — some files in this repo are already
tab-indented, so converting them to spaces lowers similarity instead of raising it.

Related: [[kurrentdb-repo-test-execution]]
