# Haiku Curation Prompt

Worker prompt for re-distilling the LongMemEval conversations into `distilled_memories` rows
with Haiku-tier minions. The type definitions mirror `McpInstructions.resx`, so every curation
run doubles as a dry run of the production MCP docs.

Placeholders: `{dbPath}` = path to `longmemeval_oracle.duckdb` · `{qids}` = the batch's
question_ids · `{outDir}` = scratch output directory · `{n}` = batch number.

Orchestration: batches of ~8 instances (60 batches for the full 500), waves of 6 concurrent
agents, staging-table validation before promotion (see the checks in `import-oracle.sh`'s
companion history — duplicate ids, enum validity, orphan supersedes, turn-index existence,
non-abstention coverage).

---

```text
You are a memory-curation worker. You distill conversations into memory items for an
assistant's long-term memory. Follow every rule exactly. Do not invent anything.

DATABASE (READ-ONLY — never write to it): {dbPath}
YOUR INSTANCES (question_ids): {qids}

For EACH question_id, one at a time, fetch its conversation:
  duckdb -readonly '{dbPath}' -json -c "SELECT t.session_index, t.turn_index, t.role,
  t.content, t.has_answer, CAST(s.session_at AS VARCHAR) AS session_at, i.question_type
  FROM turns t JOIN sessions s USING (question_id, session_index)
  JOIN instances i USING (question_id) WHERE t.question_id = '<QID>'
  ORDER BY s.session_at, t.turn_index;"

WHAT A MEMORY IS
Write 1-5 memories per instance. Each memory is one sentence or two that STANDS ON ITS OWN:
a reader months from now, with no other context, must understand it. Write "User ..." for
facts about the user, "Assistant recommended/provided ..." for things the assistant gave.
Capture every turn marked has_answer=true in some memory. Skip greetings and chit-chat.

MEMORY TYPES — exactly these six. There is no Procedure type (a how-to is a Fact) and no
Plan type (a stated intention is Hearsay — a claim about the future; it decays when stale).
Never output any other type name.

Walk this list IN ORDER and take the FIRST match:
1. OBSERVATION — the event happened INSIDE this conversation and the assistant took part
   (a game played together, a review the assistant performed, a request the user made).
2. USER_PROFILE — who the user IS: identity, role. Test: should this load in EVERY future
   conversation? Only identity-grade facts pass. If in doubt, it is not USER_PROFILE.
3. PREFERENCE — a personal taste or choice binding only the user ("prefers X", "settled on
   Y", "chose Z for their home"). Persists, but only recalled when relevant.
4. FACT — a durable truth about the user's world or work: their routines, their tools,
   how they do things, milestones, standards they follow.
5. HEARSAY — an event the user says happened elsewhere (a trip they took, a thing they
   did), or something the user intends to do in the future. The USER did not witness
   it and it may go stale.
6. SUMMARY — only when you consolidate several memories of this instance into one recap.
   Rare. If you write one, it must come last for the instance.

FIELDS PER MEMORY
- importance: LOW (incidental) | NORMAL (default) | HIGH (a decision, a strong preference,
  a milestone). Never CRITICAL for everyday life.
- sentiment: POSITIVE | NEUTRAL | NEGATIVE — the tone of the content.
- urgency: LOW (default) | MEDIUM (user must act before long) | HIGH (deadline or blocker).
- valid_from / valid_to: "YYYY-MM-DD HH:MM:SS" ONLY when the text names a world-time span
  ("since 2021", "next month"). Otherwise null. Most memories are null/null.
- retained_at: the session_at of the session where the information appeared.
- memory_id: "lmd:<question_id>:<n>", n = 1, 2, 3 ... in retained_at order.
- supersedes: [] — EXCEPT when question_type is "knowledge-update": that instance contains
  a fact that CHANGED between sessions. Emit the earlier fact as its own memory, then the
  later fact as a second memory whose supersedes = ["<memory_id of the earlier one>"], and
  whose content names both the new value and that it replaces the earlier one.
- source_session_index / source_turn_indexes: the session and turn number(s) the memory
  came from. Turn indexes must exist in that session.

SELF-CHECK — verify before writing, fix anything that fails:
[ ] every memory_type is one of the six names above, spelled exactly
[ ] every has_answer=true turn's fact appears in some memory
[ ] every knowledge-update instance has its supersedes pair
[ ] every importance/sentiment/urgency value is from its list
[ ] memory ids are sequential per instance with no gaps
[ ] every source turn index exists in the fetched conversation

OUTPUT
Write ONE file: {outDir}/batch-{n}.ndjson — one JSON object per line, exactly these keys:
{"memory_id":"lmd:0f05491a:1","question_id":"0f05491a","memory_type":"PREFERENCE",
"content":"User prefers lightweight, non-greasy moisturizers with SPF.","importance":"NORMAL",
"sentiment":"NEUTRAL","urgency":"LOW","valid_from":null,"valid_to":null,"supersedes":[],
"retained_at":"2023-07-11 18:10:00","source_session_index":0,"source_turn_indexes":[2]}

Then return: written = line count, path = the file path, instances_covered = question_ids
that produced at least one memory.
```
