---
description: Generate patch release notes from a GitHub release and add them to the current series doc
argument-hint: <version> (e.g. 26.1.1)
allowed-tools: Bash(curl https://api.github.com/:*), Bash(git branch:*), Bash(git show:*), Bash(git fetch:*), Read, Edit
---

You are adding release notes for a **patch release** to KurrentDB's documentation.

## Inputs

- **Version**: `$1` (e.g. `26.1.1`). The corresponding GitHub release lives at
  `https://github.com/kurrent-io/KurrentDB/releases/tag/v$1` (note the leading `v` on the tag).
- **Target doc**: `docs/server/release-schedule/release-notes.md` — contains the notes for **every patch in the current series** (e.g. `26.1`).
- **Style reference**: the *same document on the mature `v24.10` series* — the file `docs/server/release-schedule/release-notes.md` on the `release/v24.10` branch. It has many patches, so it shows the house style and formatting directly as markdown. This reference is the same every time.

## Steps

1. **Parse the version `$1`** (e.g. `26.1.1`). Accept it with or without a leading `v` and normalize to the bare number.
   - Full version: `26.1.1`
   - Release tag: `v26.1.1`; release URL: `https://github.com/kurrent-io/KurrentDB/releases/tag/v26.1.1`
   - Series: `26.1` (major.minor)
   - Patch number: the last component (`1` here). Patch `.0` is the initial series release; this command handles patches `.1` and up.

2. **Fetch the release content** from the public GitHub REST API with `curl` (do not scrape HTML). Keep the
   URL first on the command line — the command is only permitted for the `https://api.github.com/` host:
   ```
   curl https://api.github.com/repos/kurrent-io/KurrentDB/releases/tags/v<version> \
     -sSL -H "Accept: application/vnd.github+json"
   ```
   The JSON response contains `name` (title), `published_at` (date), and `body` (the full markdown notes:
   What's Changed, security fixes, bug fixes, features/enhancements, PR links, and `DB-####` references).
   If the response is a `Not Found` message, the tag name is wrong — re-check the version and stop.

3. **Reuse existing notes for the same change across series — check first.** The same fix is often shipped
   to more than one series, and release notes may already have been written for it in another series (the
   same `docs/server/release-schedule/release-notes.md` file on a different `release/*` branch). If so,
   **reuse that wording** rather than writing your own, so the description stays consistent across series.
   - List the release branches: `git branch -r --list 'origin/release/*'`.
   - For each *other* series, search its copy of the doc for the same change — match on the PR number, the
     `DB-####`/`GHSA` id, or the original PR number if this one is a cherry-pick:
     `git show origin/release/<series>:docs/server/release-schedule/release-notes.md`
   - If you find an entry for the same change, copy its heading and body verbatim (adjust only the PR link if
     the series shipped it under a different PR number). Only write fresh prose for changes with no existing entry.

4. **Research each change — do not just restate the PR title.** The release body gives you a one-line title
   per PR; that is rarely enough to tell the reader what they need to know. For each change you intend to
   include, dig until you understand *what actually changed and why it matters to a user* (what was broken,
   under what conditions, what the new behaviour or option is, any impact on upgrade). Sources, in order:
   - **The PR description** — fetch it (URL first, same host restriction as above):
     ```
     curl https://api.github.com/repos/kurrent-io/KurrentDB/pulls/<number> \
       -sSL -H "Accept: application/vnd.github+json"
     ```
     Read the `body`. Patch releases are often **cherry-picks** — if the body says "cherry-pick of #NNNN"
     or "backport of #NNNN", follow that link and read the **original PR** for the real detail.
   - **The commit message(s)** — `git show <sha>` or the PR's `/commits` API, when the PR body is thin.
   - **The code** — read the actual diff/files if the prose sources still don't explain the user impact.
   Write a **succinct** description (usually one or two sentences) that conveys the substance — the specific
   condition that was fixed, the behaviour that changed, or what a new option/API does and when to use it.
   Avoid restating the heading ("Fixed three bugs in X" adds nothing over a heading that already says that).
   Describe the change **from the user's perspective**: the symptom they observed and what they now see, or
   how they'd use a new option — not the internal mechanism. Prefer "secondary-index statistics are shown
   again in the admin UI" over "fixed the queries that read renamed index columns"; prefer listing a new
   option's valid values and their trade-off over naming the class that was changed. The research above is to
   ground *you*; most of the implementation detail should not reach the final sentence.

5. **Read the style reference from git** before writing — it's the same document on the mature `v24.10`
   series, so you see the exact markdown, formatting, and prose style directly:
   ```
   git show origin/release/v24.10:docs/server/release-schedule/release-notes.md
   ```
   Run `git fetch origin release/v24.10` first if the ref is missing; fall back to the local
   `release/v24.10` branch if there's no network. Study several patch entries and mirror their style.
   Conventions you should expect to confirm (verify against what you actually read):
   - Each patch is an `##` heading: `## [<version>](<release-url>)` linking to its GitHub release tag.
   - A plain date line follows the heading (e.g. `27 July 2026`) — use the release's publish date, formatted `D Month YYYY`.
   - Changes are grouped under `###` subsections with descriptive titles (e.g. a security fix, a themed group of bug fixes, a notable feature). The body explains user impact concisely — not a restatement of the title.
   - Reference issues/PRs inline where useful: `(PR [#5670](...))`. Security advisories keep their GHSA id.

   Heading conventions (apply these to every `###`):
   - **The word "Fixed" signals a bugfix — and only a bugfix.** Every bugfix heading must contain "Fixed";
     no other heading may. New features, options, or APIs use an accurate verb instead — `Added` for
     something new, `Backported` when a feature is brought into this patch from another (usually later)
     series. (e.g. `### Persistent Subscriptions: Fixed Parked Messages View in UI`,
     `### Persistent Subscriptions: Backported TruncateParked API`.)
   - **Prefix with the user-facing feature area, not the code component.** Name the area the reader knows —
     `Persistent Subscriptions`, `Secondary Indexes`, `Projections`, `Projections V2` — rather than the
     internal layer that happened to change (`HTTP API`, `UI`). A parked-messages fix that lives in the HTTP
     layer is still a "Persistent Subscriptions" change to the reader.

6. **Edit `docs/server/release-schedule/release-notes.md`.**
   - Patches are listed **newest first**. Insert the new `## [<version>]…` block directly **below the intro paragraph** and **above** the previous top patch entry.
   - Do **not** touch the frontmatter, the page title, or existing patch entries.
   - If an entry for this exact version already exists, update it in place instead of adding a duplicate.
   - Keep the writing style consistent with entries already in the file and with the reference page.

7. **Report only what you left out.** Don't restate what you added — the author will read the updated
   document to see that. Instead, if you decided to **exclude** any PR or change mentioned in the GitHub
   release from the user-facing notes, list each excluded PR (its number/title) with a one-line reason so the
   author can double-check your judgement. If nothing was excluded, say so in one line. Do not commit unless asked.

## Notes

- If the series in the doc's title doesn't match the release's series (e.g. the doc is still `26.0` but the release is `26.1.x`), stop and flag it — a new series page may be needed rather than a patch append.
- Write concise, user-facing notes. Omit changes a user would not observe: internal-only churn (dependency
  bumps with no user impact), and internal correctness/hardening fixes at an API or validation boundary that
  don't change any behaviour a user sees. Keep anything with a security or observable behavioral change.
  Report every omission per step 7 so the author can overrule your judgement.
