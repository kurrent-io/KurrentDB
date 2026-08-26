---
description: Study how Memorizer instructs agents, then design Kontext's MCP prompts and tool instructions
---

# Kontext MCP prompts and tool instructions

We are going to make Kontext's memory actually usable by an agent. Right now the tools work but
nothing teaches you how to use them well. I want to fix that properly, and before we write anything
I want you to go and learn from a system that already does this.

## Read Memorizer first

The source is at `~/dev/contrib/memorizer`. Read it, do not guess. It instructs agents on three
layers and I want all three understood:

1. **Tool descriptions** — `src/Memorizer/Tools/MemoryTools.cs` and `WorkspaceTools.cs`. Long
   imperative `[Description]` attributes with the rules written into them. How long, what they
   include, what they leave out, how they disambiguate one tool from another.
2. **MCP prompts** — `src/Memorizer/Prompts/MemorizerPrompts.cs`. Four of them:
   `memorizer_overview`, `store_memory`, `find_context`, `review_project`. What each one is for,
   what it teaches that a tool description cannot, when a client would invoke it.
3. **The result text** — every tool returns `Task<string>`. Not a DTO, not JSON. It builds
   human-readable output with a `StringBuilder`: `key: value` lines, emoji, sentences like
   `"Found 3 memories:"` and `"No results found at similarity threshold 0.7, but found 5 memories
   at relaxed threshold 0.5:"`.

Answer these, with evidence from the code:

- **Why human text instead of structured JSON?** What does that buy? My hunch is the result is an
  instruction channel of its own — it can steer the next call, explain why a search came back
  empty, or suggest a follow-up tool, and a JSON array cannot. Confirm or kill that hunch.
- What does it cost? Parsing, token weight, ambiguity, machine consumers.
- Does Memorizer coach inside its results — suggesting next tools, explaining fallbacks?
- Where do the three layers overlap or contradict each other?
- What would we copy, and what would we refuse?

## Then decide what OUR tools return

This is the consequential part, and it is why we are reading Memorizer at all. Do not leave the
text-versus-JSON question as an observation about somebody else's system — turn it into a decision
about ours.

Kontext today returns **structured content**: every tool sets `UseStructuredContent = true` and
returns a typed model class. Memorizer returns formatted text from every tool. Decide which is right
for us, and be specific about the cost of changing, because a lot sits downstream of it:

- `UseStructuredContent = true` on every `[McpServerTool]` in `McpMemoryService` and `McpRecordsService`
- the whole `Edges/Mcp/Model/` tree and the mappers that fold contracts into it
- `McpJsonContext` — the source-generated roots that make the tool schemas work trimmed and AOT,
  where the SDK's reflection fallback does not exist
- `RecalledMemory`'s lean/full polymorphism, which already loses its body over MCP

Scope it honestly: this is an MCP-edge decision only. The gRPC edge returns contract protos either
way and is not affected.

If the answer is "some of both", say exactly which tools return what and why the line falls there.
A hybrid that nobody can state the rule for is worse than either pure option.

## Then design ours

Kontext is not Memorizer. It has no workspaces, no projects, no titles, and no update — corrections
are a new memory with `supersedes`. Anything you carry over has to be re-derived against our model,
not translated. `src/Kontext/Kurrent.Kontext/Modules/Memory/Edges/Mcp/McpPrompts.cs` is a draft I
made from Memorizer's shape and it is wrong on every one of those points. Bin it.

Our surface: `retain` · `recall` · `reclaim` · `recollect` · `reinforce`, plus `search` and `query`
over the records. All agent-facing text lives in `McpInstructions.resx`, applied at registration.

The prompt set to start from — one per real workflow, not one per tool, because the tool
descriptions already carry the per-tool rules. Prompts should teach **sequence and judgement**:

| Prompt | Teaches |
|---|---|
| `kontext_start_task` | recollect OPEN_QUESTIONs as a work queue, recall the subject, reinforce what actually helped |
| `kontext_retain` | the write rule — name what would show you wrong, attribution inside the content, ONE memory per thing that can die on its own |
| `kontext_correct` | supersede a live tip; reclaim it first, because the successor carries the union of its citations |
| `kontext_curate` | the fold pass — recall, judge duplicates yourself, retain the survivor with the loser in `supersedes` |
| `kontext_investigate` | `search` (by meaning, payloads included) vs `query` (SQL, exact counts) over the log |

Challenge that set. Add, cut, or merge if the Memorizer read says something better.

## Then revisit the tool instructions

Once the prompts exist, re-read `McpInstructions.resx` against them. Whatever a prompt now teaches
should come out of the tool text, and whatever the tool text still has to say should be sharpened.
The two must not repeat each other and must not disagree.

## Rules

- Read the source. Every claim about Memorizer cites a file and a line.
- Write in ASD-STE100 for anything an agent reads. Short sentences, active voice, imperative, one
  instruction per sentence.
- Do not describe how our tools work in a prompt — the tool descriptions do that. Prompts say when
  and in what order.
- Show me the prompts before you wire them up.
