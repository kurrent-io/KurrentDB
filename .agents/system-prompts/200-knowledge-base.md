<!-- <knowledge-base> -->

# Knowledge Base

> How to consult, create, and organize project knowledge. The knowledge base lives at `.claude/context/`
> and is the canonical home for development-session documentation — feature work, research, reports,
> postmortems, and references. Root `CLAUDE.md`, `CONTEXT.md`, and agent memory are separate durable
> stores; this is not the only place knowledge lives.

> **Operations are a skill.** Scaffolding a feature, filing a doc, regenerating the index, archiving, and
> auditing are handled by the `devx-knowledge-base` skill — this document is the *model* (where docs go and
> how they are shaped); the skill is the *doer*. Reach for it whenever you create or maintain KB docs.

## Directory Structure

```text
.claude/context/
├── project/            # living: codebase reference docs (no date prefix)
├── playbook/           # living: team workflows and processes (no date prefix)
├── docs/
│   ├── .templates/     # static skeletons for feature artifacts (design/prd/spec/plan)
│   ├── features/       # feature work — one folder per feature (design space → prd/spec/plans)
│   ├── research/       # standalone investigations and spikes — cross-cutting findings
│   ├── reports/        # standalone point-in-time reports — audits, security, perf, drift sweeps
│   └── postmortem/     # standalone incident postmortems
├── external/           # imported external docs (read-only, mirrors source structure)
└── .scratch/           # throwaway files; never promote to permanent docs

.claude/plans/          # Claude Code plan-mode scratch (auto-generated, ephemeral)
```

Each subdirectory has a defined purpose. Never place files directly in `docs/` or `context/`.

> **`docs/` at the repo root is the project's public documentation website** — user-facing SDK docs, guides,
> and API references published for consumers. It is NOT part of the knowledge base. Read from `docs/` when you
> need official SDK documentation or must update user-facing content. Write to `.claude/context/docs/` when
> producing internal development artifacts. Never confuse the two.

TRIPWIRE: About to write a file to `docs/` at the repo root. Is this a user-facing SDK documentation
update? If not — stop. Internal artifacts go under `.claude/context/docs/`.

## Feature Work — the `features/` folder

Feature work is organized **vertically**: one folder per feature holds everything about that feature. This
optimizes for the expensive operation — reconstructing a feature from its scattered parts. Cross-feature
queries ("show me all specs") are cheap globs (`features/*/spec/spec.md`); feature reconstruction from
type-scattered folders is not. Group by feature, glob by type.

```text
docs/features/2026-07-09-1432-checkpoint-persistence/
├── design/
│   ├── design.md       # the design SPACE (see below) — always present, born with the feature
│   ├── spike-*.cs      # spike code, scripts, POCs — feature-tied exploration lives here
│   └── refs/           # sources owned by the design space
├── prd/                # distilled from the design space once it settles
│   ├── prd.md
│   └── refs/
├── spec/               # distilled from the design space once it settles
│   ├── spec.md
│   └── refs/
└── plans/              # release-level scope, derived from the PRD's releases + the spec
    ├── plan.mvp.md
    ├── plan.r1.md
    └── refs/
```

### The four artifacts

| Artifact  | Role                    | Nature                                                         | Template                          |
|-----------|-------------------------|----------------------------------------------------------------|-----------------------------------|
| `design/` | **Design space** — brainstorm, discussion, decision log | Deliberately informal, **append-leaning**, kept for the feature's life. The *input*. | `docs/.templates/design.md` |
| `prd/`    | Product requirements — WHY / WHAT | Clean, current-state, rewritten as understanding changes. The *output*. | `docs/.templates/prd.md`    |
| `spec/`   | Technical spec — HOW | Clean, implementation-grade, kept in sync with the code. The *output*. | `docs/.templates/spec.md`   |
| `plans/`  | Release scope & sequencing — WHEN | One file per release (`plan.mvp.md`, `plan.r1.md`). The *output*. | `docs/.templates/plan.md`   |

**The design space is the keystone.** It is not a formal design document — it is the working doc where you
think out loud, capture discussion, and record decisions. It is born with the feature (a feature folder
starts as just `design/design.md`) and kept forever. Once it settles, you **distill** its outcome into
`prd/` and `spec/`, and slice releases into `plans/`. The design space keeps evolving; the prd/spec are
rewritten to reflect current truth. Because the design space retains the decision history and the rejected
alternatives, it *is* the project's lightweight decision record — read it to answer "why is it built this way?"

**Spikes and POCs live in the design space.** Feature-tied throwaway exploration — spike code, scripts,
proof-of-concept programs — goes in `design/`, next to `design.md`, not scattered in the source tree. Reserve
`.scratch/` for throwaway that belongs to no feature.

### Conventions

- **Feature folder name:** `YYYY-MM-DD-HHMM-<slug>`. The minute component preserves ordering when several
  feature folders are created on the same day. The timestamp lives on the **folder**; child docs are named
  by type (`design.md`, `prd.md`, `spec.md`) and inherit the folder's ordering.
- **Plans keep `plan` in the filename:** `plans/plan.mvp.md`, `plans/plan.r1.md`.
- **`refs/` is owned by its artifact.** A source the spec cites is the spec's; it goes in `spec/refs/`, not a
  shared feature-level folder. Ownership must stay legible.
- **Graduated:** the folder is born as `design/`; `prd/`, `spec/`, `plans/` appear only when the work matures
  enough to distill them. A one-paragraph feature is just `design/design.md`.
- **`refs/` (feature-local) vs `research/` (cross-cutting):** a spike that matters only to this feature →
  `<artifact>/refs/`. A durable investigation you cite across features → the top-level `research/` category.
- **`docs/features/INDEX.md` is generated.** A table of every feature (active + archived), derived from
  frontmatter by the `devx-knowledge-base` skill — never hand-edit it; regenerate after adding or restatusing
  a feature.

## Standalone Snapshots

Work that no single feature owns lives in its own dated category. Each document gets its own dated folder
(`YYYY-MM-DD-HHMM-<slug>/`) so companion files (diagrams, data, notes) sit beside the main doc.

```text
docs/research/2026-07-09-0930-log-position-semantics/
├── research.md         # main document (required)
└── refs/               # companion files (optional)

docs/reports/2026-07-09-1100-xmldoc-drift/
└── report.md

docs/postmortem/2026-07-09-1400-tls-hang/
└── postmortem.md
```

| Category   | Folder pattern                               | Main file       |
|------------|----------------------------------------------|-----------------|
| Research   | `docs/research/YYYY-MM-DD-HHMM-<topic>`      | `research.md`   |
| Report     | `docs/reports/YYYY-MM-DD-HHMM-<topic>`       | `report.md`     |
| Postmortem | `docs/postmortem/YYYY-MM-DD-HHMM-<incident>` | `postmortem.md` |

## Living Docs

`project/` and `playbook/` are flat, living documents — no date prefix, updated in place.

- `project/<topic>.md` — codebase reference (architecture, module organization, conventions).
- `playbook/<topic>.md` — team process and workflow.

## Templates

`docs/.templates/` holds the skeletons for the four feature artifacts (`design`, `prd`, `spec`, `plan`).
These are static stationery, **not living documents**: copy a template into a feature folder and fill it in.
Do not edit the originals unless you are deliberately revising the house skeleton.

## Frontmatter

**Feature artifacts** (`design`, `prd`, `spec`, `plan`) — co-authorable, so `authors` is a **list**:

```yaml
---
title: Checkpoint Persistence
status: exploring        # live states — design: exploring|settling · prd/spec: draft|review|accepted · plan: proposed|active|shipped
authors: [sergio]
date: 2026-07-09
tags: [streaming, duckdb]
superseded_by: 2026-08-01-1200-checkpoint-v2   # optional — when a newer feature replaces this one
---
```

**Research** — single-owner, so `author` is a **scalar**; investigative, so no lifecycle `status`:

```yaml
---
title: KurrentDB Log-Position Semantics
type: research           # research | spike | investigation
date: 2026-07-09
author: sergio
tags: [streaming, log-position]
---
```

**Reports** — immutable point-in-time snapshots, single-owner, no `status`:

```yaml
---
title: XML-Doc Drift Sweep — Streaming Projections
type: audit              # audit | security | performance | drift | review | analysis
date: 2026-07-09
author: sergio
tags: [xmldoc, streaming]
scope: public                            # optional — what was examined
related: [2026-06-07-1200-public-audit]  # optional
supersedes: 2026-05-01-0900-xmldoc-drift # optional — newer report replacing an older one
---
```

**Postmortems** — `title`, `date`, `author` required; `tags` encouraged.

Convention on authorship: feature artifacts use `authors` (a list — they are co-authorable); standalone
snapshots use `author` (a scalar — they have a single owner). This split is deliberate.

**Lifecycle & archival.** A feature's end-of-life is expressed in its `status`, not by moving folders:
`superseded | deprecated | removed | abandoned | withdrawn` (set it on the `spec`, else `prd`, else `design`).
Those values move the feature into the **Archived** section of the generated features index; add
`superseded_by: <feature-slug>` when a successor exists. The `devx-knowledge-base` skill sets status and
regenerates the index.

## Document Skeletons

Feature artifacts (`design`, `prd`, `spec`, `plan`) have full skeletons in `docs/.templates/` — copy the
template, do not hand-roll the structure. Standalone snapshots use the skeletons below.

`research.md`:

```markdown
## Question
What we're trying to find out, and why.

## Findings
What the investigation turned up — evidence, measurements, references.

## Implications
What this means for the design or the code. Omit if purely informational.
```

`report.md`:

```markdown
## Summary
What was examined, the headline outcome, and the verdict in a few sentences.

## Findings
The detailed results — tables, severity groupings, per-item detail.

## Recommendations
What to do about the findings. Omit if the report is purely informational.

## Method
Scope, tools, and what was and was not covered — so the run is reproducible and its limits are clear.
```

`postmortem.md`:

```markdown
## Incident Summary
What happened, when, and impact.

## Timeline
Chronological sequence of events.

## Root Cause
What caused the incident.

## Lessons Learned
What we'll do differently.
```

## Rule 1 — Consult Before Creating

Before starting work on any topic — feature design, bug investigation, research, implementation — search
the knowledge base for existing documentation. Prior sessions may have already explored the problem space,
written a design space, or documented decisions that constrain the current work.

**Search procedure:**

1. `find .claude/context/ -name "*<topic>*" -o -name "*<keyword>*"` — check for folders and files by name
2. `grep -rl "<keyword>" .claude/context/` — search file contents for relevant terms
3. Read any matches before proceeding

If existing docs cover the topic, build on them. Reference the doc path in conversation so the user knows
what prior knowledge informed your approach. If they conflict with the current request, surface the conflict
— don't silently override prior decisions.

TRIPWIRE: You are about to start a feature, design, or research effort. Have you searched `.claude/context/`
for existing docs on this topic? If you cannot cite the search you ran, run it now.

## Rule 2 — File in the Right Place

When producing documentation during a session, determine the category and create the folder. Paths are
absolute from the repo root — the `.claude/context/` prefix is mandatory, never bare `docs/...`.

| What you're writing                    | Category     | Create at                                                              |
|----------------------------------------|--------------|------------------------------------------------------------------------|
| Feature work (design space → prd/spec/plans) | `features` | `.claude/context/docs/features/YYYY-MM-DD-HHMM-<slug>/` — start at `design/design.md`, add `prd/` `spec/` `plans/` as it matures |
| Investigation, spike, cross-cutting research | `research` | `.claude/context/docs/research/YYYY-MM-DD-HHMM-<slug>/research.md`      |
| Audit, security/perf/drift review      | `reports`    | `.claude/context/docs/reports/YYYY-MM-DD-HHMM-<slug>/report.md`        |
| Incident analysis                      | `postmortem` | `.claude/context/docs/postmortem/YYYY-MM-DD-HHMM-<slug>/postmortem.md` |
| Codebase reference                     | `project`    | `.claude/context/project/<topic>.md` (flat, living doc)               |
| Team process / workflow                | `playbook`   | `.claude/context/playbook/<topic>.md` (flat, living doc)              |
| Imported external docs                 | `external`   | `.claude/context/external/<source>/` (mirrors source structure)       |

EXCEPTION — experimentation: during prototyping, spikes, or exploratory work, the **design space is the one
place you may record findings**. Capture them in the feature's `design/design.md` and put spike code / POCs
alongside it. Do NOT create or update any other knowledge-base doc (prd, spec, reports, project, playbook)
until the work settles or you're explicitly asked.

TRIPWIRE: You are about to write a doc to `.claude/context/`. Does the path match the table above, with the
full `.claude/context/` prefix? If not, correct it before continuing.

TRIPWIRE: You are about to write a doc inline in conversation instead of to the knowledge base. Unless you are
mid-experiment (record those in the design space per the exception above), stop — write it to the appropriate
category folder and cite the path.

TRIPWIRE: The user asked for a report — an audit, security review, perf analysis, drift sweep, or any findings
write-up. Do not dump it only into the chat. Write it to `docs/reports/YYYY-MM-DD-HHMM-<topic>/report.md`, then
summarize in conversation, citing the path.

## Rule 3 — Keep Docs Current

`project/` and `playbook/` are living documents — update them in place when you find them outdated or
incomplete while working on a related task.

Within a feature folder, `prd/` and `spec/` are **current-state**: rewrite them as truth changes. `design/`
is **append-leaning**: add discussion and mark decisions; do not rewrite the history of the deliberation.

Standalone snapshots (`research`, `reports`, `postmortem`) are point-in-time. If a snapshot's subject
evolves, write a new dated one and set `supersedes` on the replacement rather than rewriting the original.

## Rule 4 — External Docs Are Read-Only

Files in `external/` are imported from outside sources (KurrentDB server docs, vendor references, API specs).
Never edit them directly. If you need to annotate or extend external docs, create a companion feature or
research doc that references the external doc.

## Importing External Documentation

When the user asks to import external documentation into the knowledge base:

1. Determine the source and scope (URL, local path, specific pages vs full site)
2. Place imported files under `external/` mirroring the source structure
3. Preserve the original file names and directory hierarchy
4. Add a `README.md` at the import root documenting the source URL, import date, and scope
5. Do not modify the imported content — keep it verbatim

## Importing Existing Design Docs

When asked to bring pre-existing design docs, PRDs, specs, or plans into the knowledge base — migrating loose
or pre-KB docs into the feature model:

1. Identify the feature each doc belongs to, and its artifact type (design / prd / spec / plan).
2. Create or locate the feature folder `docs/features/YYYY-MM-DD-HHMM-<slug>/`. Use the doc's original date for
   the timestamp when known; otherwise the import date.
3. Place the doc as `<type>/<type>.md`, and move its companion files into `<type>/refs/`.
4. Normalize the frontmatter to match the type (see Frontmatter). Preserve the original body — do not rewrite
   the content; if you must reshape it, keep the original intent readable.
5. A loose idea or brainstorm with no distilled output becomes the feature's `design/design.md`.
6. If several docs describe one feature, colocate them in the same feature folder rather than creating
   duplicates.

<!-- </knowledge-base> -->
