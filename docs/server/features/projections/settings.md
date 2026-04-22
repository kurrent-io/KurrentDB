---
order: 4
---

# Projections settings

Settings in this section concern projections that are running on the server. These settings are specified as [database configuration](../../configuration/configuration.md).

## Run projections

The `RunProjections` option tells the server if you want to run all projections, only system projections or no
projections at all. Keep in mind that the `StartStandardProjections` setting has no effect on custom
projections.

The option accepts three values: `None`, `System` and `All`.

When the option value is set to `None`, the projections subsystem of KurrentDB will be completely disabled
and the Projections menu in the Admin UI will be disabled.

By using the `System` value for this option, you can instruct the server to enable system projections when the
server starts. However, system projections will only start if the `StartStandardProjections` option is set
to `true`. When the `RunProjections` option value is `System` (or `All`) but the `StartStandardProjections`
option value is `false`, system projections will be enabled but not start. You can start them later manually
via the Admin UI or via an API call.

Finally, you can set `RunProjections` to `All` and it will enable both system and custom projections.

| Format               | Syntax                       |
|:---------------------|:-----------------------------|
| Command line         | `--run-projections`          |
| YAML                 | `RunProjections`             |
| Environment variable | `KURRENTDB_RUN_PROJECTIONS`  |

**Default**: `None`, all projections are disabled by default.

Accepted values are `None`, `System` and `All`.

## Start standard projections

The `StartStandardProjections` option controls whether the five built-in
[system projections](system.md) are started automatically when the projections subsystem is brought up:

- `$by_category`
- `$stream_by_category`
- `$streams`
- `$by_event_type`
- `$by_correlation_id`

When this option is `true`, any standard projection that is currently in the `Stopped` state is transitioned
to `Running` shortly after the subsystem starts. Projections that have been explicitly disabled in a previous
run will be re-enabled — the setting drives state on each startup, it does not persist across restarts on the
projection itself.

When this option is `false` (the default) the five standard projections exist but stay `Stopped`. Enable them
on demand, either from the admin UI or via the HTTP API:

```bash:no-line-numbers
curl -i -X POST "http://{host}:{http-port}/projection/$by_category/command/enable" \
  -H "Content-Length:0" -u admin:changeit
```

| Format               | Syntax                                     |
|:---------------------|:-------------------------------------------|
| Command line         | `--start-standard-projections`             |
| YAML                 | `StartStandardProjections`                 |
| Environment variable | `KURRENTDB_START_STANDARD_PROJECTIONS`     |

**Default**: `false`

::: tip
The setting only takes effect when the projections subsystem is loaded — i.e. `RunProjections` is `System`
or `All`. With `RunProjections=None` the subsystem is off entirely and `StartStandardProjections` is
ignored.
:::

::: tip
Running the server with `--dev` implicitly enables `StartStandardProjections` (and forces
`RunProjections` to at least `System`) so the standard projections are available for local exploration.
:::

::: warning
Running the standard projections adds write amplification — every appended event produces link events
into the category, event type, and (if enabled) correlation-id index streams. See
[Performance impact](README.md#performance-impact). Turn them on deliberately, not "just in case".
:::

::: tip
For the category and event type cases, prefer the built-in
[secondary indexes](../indexes/secondary.md) (v25.1+) over `$by_category` / `$by_event_type` — they provide
the same query capability without link-event write amplification or unresolvable-link storage bloat.
:::

## Projection threads

Projection threads are used to make calls in to the V8 JavaScript engine, and coordinate dispatching
operations back into the main worker threads of the database. While they carry out none of the operations
listed directly, they are indirectly involved in all of them.

The primary reason for increasing the number of projection threads is projections which perform a large amount
of CPU-bound processing. Projections are always eventually consistent - if there is a mismatch between egress
from the database log and processing speed of projections, the window across which the latest events have not
been processed promptly may increase. Too many projection threads can end up with increased context switching
and memory use, since a V8 engine is created per thread.

There are three primary influences over projections lagging:

- Large number of writes, outpacing the ability of the engine to process them in a timely fashion.
- Projections which perform a lot of CPU-bound work (heavy calculations).
- Projections which result in a high system write amplification factor, especially with latent disks.

Use the `ProjectionThreads` option to adjust the number of threads dedicated to projections.

| Format               | Syntax                          |
|:---------------------|:--------------------------------|
| Command line         | `--projection-threads`          |
| YAML                 | `ProjectionThreads`             |
| Environment variable | `KURRENTDB_PROJECTION_THREADS`  |

**Default**: `3`

## Fault out of order projections

It is possible that in some cases a projection would get an unexpected event version. It won't get an event
that precedes the last processed event, such a situation is very unlikely. But, it might get the next event
that doesn't satisfy the `N+1` condition for the event number. The projection expects to get an event
number `5` after processing the event number `4`, but eventually it might get an event number `7` because
events `5` and `6` got deleted and scavenged.

The projections engine can keep track of the latest processed event for each projection. It allows projections
to guarantee ordered handling of events. By default, the projections engine ignore ordering failures like
described above. You can force out of order projections to fail by setting the `FailOutOfOrderProjections`
to `true`.

| Format               | Syntax                                      |
|:---------------------|:--------------------------------------------|
| Command line         | `--fault-out-of-order-projections`          |
| YAML                 | `FaultOutOfOrderProjections`                |
| Environment variable | `KURRENTDB_FAULT_OUT_OF_ORDER_PROJECTIONS`  |

**Default**: `false`
