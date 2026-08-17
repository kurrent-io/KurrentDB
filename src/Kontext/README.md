# Kurrent Kontext

Long-term memory for AI agents.

An agent connected to Kontext remembers what it learned yesterday, knows who and what those memories are about, and gets back exactly the few memories that matter when it asks.

```mermaid
flowchart LR
    Agent["🤖 Your agent"] -->|"“remember this”"| K["🧠 Kontext"]
    K -->|"the right memories,<br/>ranked and balanced"| Agent
    K --- Bank[("everything it has<br/>ever learned")]
```

## Six verbs

The whole surface is six verbs, spoken over MCP or gRPC:

| Verb | What it does |
|---|---|
| **retain** | save something worth remembering |
| **recall** | ask a question, get the most relevant memories back |
| **reclaim** | fetch specific memories by id |
| **recollect** | browse the memory bank, filtered and sorted |
| **retract** | take something back, along with everything built on it |
| **reflect** | distill many memories into fewer, better ones |

## What happens when it remembers

Every memory is written to a permanent journal before anything else. The organized views are built from that journal, so they can always be rebuilt, audited, or replayed.

```mermaid
flowchart TD
    A["a memory arrives<br/>“the acme deal shipped, ping Jenny”"] --> J[("the journal<br/>recorded first, kept forever")]
    J --> B["the memory bank<br/>filed with its trust level,<br/>its clocks, and its tags"]
    J --> C["names are noticed<br/>Jenny · Acme"]
    C --> D["the address book<br/>one entry per real person,<br/>company, repo, project"]
```

Each memory carries three things that matter later:

- **A kind that doubles as a trust level.** Something the agent saw happen outranks something it was told. A rumor is welcome, but it is stored as a rumor and ranked as one.
- **Three clocks.** When it happened, when it was saved, and when it last proved useful. Memories that keep being useful stay fresh, memories that never help fade quietly.
- **Tags for scope.** This user, this repo, this project. Ask within a scope and other scopes stay out of the way.

## The address book, and honest doubt

Kontext keeps one entry per real thing and files every memory under the entries it mentions. The interesting part is what happens when it is not sure.

```mermaid
flowchart TD
    N["a name arrives: “Emilia Chen”"] --> Q{"do we already<br/>know this name?"}
    Q -->|"clearly yes"| F["file it under the entry we have,<br/>learn the new spelling"]
    Q -->|"clearly no"| G["open a fresh entry"]
    Q -->|"too close to call, looks<br/>a lot like “Emily Chen”"| H["keep both, leave a note:<br/>“these two might be the same”"]
    H --> R["later, one human answer<br/>settles it for good"]
    R -->|"“same person”"| S["merged, the spelling becomes<br/>a nickname, memories reunited"]
    R -->|"“different people”"| T["noted forever,<br/>never asked again"]
```

Merging two entries can never be undone, so Kontext refuses to guess at save time, when it has the least evidence it will ever have. It keeps both entries, leaves a note, and moves on. One human answer later fixes memory permanently, and the same question is never asked twice.

## What happens when you ask

```mermaid
flowchart TD
    Q["“where does Emily live?”"] --> S["search by meaning<br/>and by keywords"]
    S --> W["weigh every candidate:<br/>how trustworthy, how fresh,<br/>how important"]
    W --> E["add clues from names:<br/>memories about Emily move up,<br/>memories under a “might be the same”<br/>note move up a little too"]
    E --> V["keep the results varied<br/>and balanced, no single kind<br/>of memory crowds out the rest"]
    V --> R["a handful of memories,<br/>best first"]
```

The doubt notes earn their keep here. Memories filed under a maybe-the-same name still reach you, just at reduced strength. If the doubt was justified you lost nothing, and if it wasn't, the weak push fades against real answers. Unresolved doubt costs almost nothing while it waits.

## Nothing is silently lost

- New knowledge **supersedes** the old. The fresh version answers questions, the old one stays underneath as history.
- **Retracting** a memory hides it and everything derived from it, but it can still be inspected on request.
- The journal keeps every event, so the entire memory can be rebuilt from scratch at any time.

```mermaid
flowchart LR
    V1["“Emily lives in Oslo”"] -->|"superseded by"| V2["“Emily moved to Berlin”"]
    V2 --> A["what recall sees"]
    V1 -.-> H["history, always inspectable"]
```

Kontext runs on KurrentDB. The journal is a real event log, not a metaphor.
