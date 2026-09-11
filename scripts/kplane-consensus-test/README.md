# Kontrol Plane consensus test

An election stress test for a KurrentDB cluster running in **Kontrol Plane mode**. A three-node
secure cluster is put under continuous write load while [Pumba][pumba] crashes and freezes nodes
underneath it, and a checker decides whether any consensus invariant was violated.

Failure modes exercised: **crash** (SIGKILL), **hang** (cgroup freeze), **resign** (leadership
handed over on request).

[pumba]: https://github.com/alexei-led/pumba

## Quick start

```bash
cp .env.example .env        # then set KDB_LICENSE_KEY
./run.sh
```

`./run.sh crash` / `./run.sh hang` isolate one fault type; `./run.sh ""` is the baseline with no
Pumba at all.

The exit code is the verdict: `0` if every invariant held, `1` otherwise. Results are printed as a
table by the `checker` container at the end of the run, followed by a restart count per node and
the state of the chaos containers.

`run.sh` runs Compose **detached** on purpose: `up --abort-on-container-exit` would tear the whole
run down the first time Pumba killed a node, which it does by design. The verdict is read back
from the checker container with `docker inspect` instead.

Clean up is automatic; `docker compose --profile chaos down -v` if a run is interrupted.

## What the pieces do

| Service | Role |
|---|---|
| `node1-3` | KurrentDB in Kontrol Plane mode, built from the working tree |
| `supervisor` | Restarts nodes Pumba killed — see below, `restart: always` cannot do it |
| `writer` | Continuous concurrent appends. Scale with `--scale writer=4` |
| `checker` | Polls gossip, drives resignations, then runs the invariants and **owns the exit code** |
| `chaos-crash` | Pumba: `kill --signal SIGKILL` on a random node every `CHAOS_CRASH_INTERVAL` |
| `chaos-hang` | Pumba: `pause` a random node for `CHAOS_HANG_DURATION` every `CHAOS_HANG_INTERVAL` |

Pumba is a fault injector, not a test runner: it never looks at the cluster, and its own exit code
only reflects whether Pumba itself worked. That is why the verdict lives entirely in the checker.
Pumba also cannot resign a leader, since that is an application operation — the checker does it.

### Chaos profiles

Without a profile you get a **baseline** run: load and resignations, no crashes or freezes. Useful
for confirming the harness is clean before trusting a failure.

```bash
./run.sh ""        # baseline: resignations only
./run.sh crash     # + SIGKILL
./run.sh hang      # + freeze
./run.sh chaos     # both (default)
```

Prefer isolating a single fault type when chasing a failure. Pumba is **not quorum-aware**: with
both profiles on, two faults can overlap and take out two of three nodes, which looks like a bug
but is not one. The default intervals make that rare rather than impossible.

Overlap has a second, nastier effect. If `chaos-crash` kills a node that `chaos-hang` has frozen,
the unpause fails (`Container ... is not paused`) and Pumba treats that as **fatal** — the chaos
container exits and the rest of the run has no faults at all, while the checker carries on and may
still report a pass on the strength of resignations alone. Both chaos services therefore run with
`--skip-error`, which retries on the next tick instead of exiting.

### Why a supervisor, and not `restart: always`

The most surprising thing about combining Pumba with Compose: **a `restart` policy will not bring
back a node Pumba killed.** Docker deliberately ignores a container's restart policy when the
container was stopped through the API, and Pumba's `kill` is an API call, not a signal from inside.
`docker inspect` confirms it — `RestartCount=0`, `policy=always`, container `exited`.

Left unnoticed this quietly voids the whole run: the cluster loses one node per kill, has no quorum
after the second, and the checker keeps measuring an empty cluster for the remaining time. It shows
up as a huge `leaderless` figure that looks like a product bug.

So `kplane-supervisor` polls every two seconds and `docker start`s any node found exited.
`restart: always` is kept as well, because it *does* cover the case that matters most — a node that
died on its own, which is a real crash rather than an injected one.

A chaos container dying does not abort the run — nothing is watching it. `run.sh` therefore prints
the state of the chaos containers at the end, and an `Exited` one means every fault after that
point was never injected. Treat a pass in that situation as no result at all.

## Why freeze, and not just kill

`pause` is the cgroup freezer. Every thread stops but the container stays up and its sockets stay
open, so peers see a stall and then a zero-window — never a reset. That is a long GC pause or a
stalled disk, and it is the case consensus protocols get wrong.

For the Kontrol Plane specifically: freeze the database leader for longer than
`KontrolPlaneAppointmentTimeoutMs` (1000 ms) and it cannot renew its appointment, so the Kontrol
Plane must fence it and appoint someone else. **When the node thaws it must discover it has been
fenced and freeze itself rather than acknowledge anything.** That single scenario is worth more
than the rest of the crash testing put together.

## Invariants

Safety — a single violation fails the run:

| # | Invariant | How it is checked |
|---|---|---|
| A0 | Chaos actually happened | At least two distinct epochs were appointed. A run with no leadership change proves nothing |
| S1 | One appointee per epoch | Group appointment events by `epoch`; more than one distinct appointee is a split brain |
| S2 | Epochs advance | Each epoch's *first* sighting anywhere must be later than the previous epoch's |
| S3 | No offline truncation | `OFFLINE TRUNCATION IS NEEDED` in any node log |
| S4 | No acknowledged write lost | Every stream's ledger high-water mark is still readable |
| S5 | No legacy elections | Any `ELECTIONS:` line means the cluster is not on the Kontrol Plane path at all |
| S6 | No unexpected crash | Pumba's kills are silent, so any `Fatal` or unhandled exception is a crash nobody asked for |
| S7 | No very slow queue | `VERY SLOW QUEUE MSG`. Set `ENFORCE_SLOW_QUEUE=false` under deliberate overload |

Liveness — budgets, since every fault causes some unavailability:

| # | Invariant | Default |
|---|---|---|
| L1 | Cumulative leaderless time | ≤ `MAX_LEADERLESS_PERCENT` (15%) of the run |
| L2 | Longest single outage | ≤ `MAX_SINGLE_OUTAGE_SECONDS` (45s) |

The clock starts only once the cluster has formed, so ordinary startup is not charged against the
budgets.

### How the checks read the logs

KurrentDB already writes structured logs in Serilog's compact format
(`{ {@t, @mt, @r, @l, @i, @x, ..@p} }`) to `<logdir>/<component>/log.json`, at `Debug` by default.
The checker matches on the message **template** (`@mt`) and reads typed properties such as `epoch`
and `leaderAddress`, rather than scraping rendered text — so property values never affect a match.

One gap worth closing in the product: `RaftKontroller.Appointment.cs` builds its
`... is appointed as leader ... Chosen from: ...` line with string interpolation rather than a
Serilog template, so the single most interesting line for auditing an appointment arrives as one
opaque string with no properties. Converting it to a real template would let the checker verify
*why* a candidate was chosen, not just that it was.

### The write ledger

Each writer task owns its own streams, so acked revisions on a stream are monotonic and a
high-water mark per stream is sufficient: if revision R was acked on stream S, every revision up to
R must still be readable. Verification is then one read per stream rather than one per event.

Only **successful** appends are recorded. A timed-out or ambiguous append may well have committed,
but the ledger makes no claim about it — it is deliberately a lower bound on what the cluster must
still have.

## Tuning the load

Write *rate* matters here for a specific reason: the dangerous window is the few milliseconds
during which a leader is being replaced, and if no append is in flight then the window is never
occupied. **Concurrency matters more than raw rate** — a sequential writer has exactly one append
outstanding at any moment.

- `CONCURRENCY` (default 32) — parallel append tasks per writer
- `--scale writer=N` — more writer containers
- `EVENT_SIZE_BYTES` — payload size

Two profiles worth running:

- **Steady** — load the cluster comfortably handles. Every violation is a real bug.
- **Overload** — deliberately saturate, so nodes miss their appointment renewals. This is a "hang"
  arriving by a more realistic route than SIGSTOP. Expect churn: assert only the safety invariants
  and set `ENFORCE_SLOW_QUEUE=false`.

## Configuration

The node configs in `conf/` are JSON (valid YAML, which is what the server parses) and are adapted
from a known-good QA Kontrol Plane setup. Notable settings:

- `IsKontrolPlaneNode` / `IsDataPlaneNode` — both true on every node; the validator currently
  requires that for non-RoR nodes
- `KontrollerPort: 3113` — the Raft transport, a **separate port** from the gRPC API on 2113.
  `KontrolPlaneBootstrapSeed` uses 3113, `KontrolPlaneApiSeed` uses 2113
- `KontrolPlaneAppointmentTimeoutMs: 1000`, election timeouts 700/1000 ms — the current defaults
- `SkipDbVerify` / `SkipIndexVerify` — restarts are frequent here
- `RunProjections: None` — keeps the signal on consensus. Turn on to also exercise
  projections across failover
- TLS is **on**, which also exercises the new shared `NodeTlsPolicy` / `NodeSslOptions` paths and
  the Kontroller's own Raft TLS

The license key lives in `.env` as `KDB_LICENSE_KEY` and is mapped to
`KURRENTDB__LICENSING__LICENSE_KEY` for the containers. It is deliberately *not* named
`KURRENTDB_*`: the server parses every such variable as an option and `AllowUnknownOptions` is
false, so a stray prefixed variable would stop the node from starting. `.env` is gitignored.

## Troubleshooting

**Confirm what Pumba will hit** before trusting a run — the regex is anchored (`^kplane-node[1-3]$`)
so it cannot match the writers, the checker, or Pumba itself, but a typo is cheap to catch:

```bash
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/alexei-led/pumba:latest \
  --dry-run --log-level=info kill "re2:^kplane-node[1-3]$"
```

Note that Pumba matches by **container name**. These are set explicitly (`kplane-node1`…), so
dropping `container_name` would silently break the targeting.

**S5 fails** — the cluster came up on legacy elections, so nothing under test was exercised. Check
`IsKontrolPlaneNode` / `IsDataPlaneNode` actually reached the server.

**A0 fails** — no leadership ever changed. Either chaos was not enabled (no `--profile`) or the run
was too short for the intervals.

**Everything fails at once** — check `docker compose logs checker` for the startup phase; if the
cluster never formed the checker exits before any chaos runs.

**L1/L2 fail and the cluster never recovers a leader** — the crash interval is shorter than the
time a node needs to restart and rejoin, so quorum is lost and never regained. Raise
`CHAOS_CRASH_INTERVAL` above the observed rejoin time. If `run.sh` reports **0 supervisor
restarts** after a chaos run, nodes are not coming back at all and nothing after the first few
kills was measured — treat the run as void.

Logs are kept on the host in `logs/node{1,2,3}/` and ledgers in `ledger/`, so both survive
`docker compose down` for post-mortem. They are wiped at the *start* of the next run: a node
appends to the same `log.json` across restarts, so logs left over from an earlier run would
otherwise be read back as appointments that happened during this one.
