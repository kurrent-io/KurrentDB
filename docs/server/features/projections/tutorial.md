---
order: 3
---

# Projections tutorial

In this tutorial you write a projection that counts how many Xbox One S consoles customers added to their shopping carts. You run it on your machine with no database involved, then deploy it to KurrentDB.

You author it with [gaffer](https://gaffer.kurrent.io), a command-line tool for developing, testing and deploying projections. Gaffer is a standalone tool rather than a client library, and it runs the same JavaScript engine the server does, so what you see locally matches what the server does with the same events.

## Before you start

Install gaffer:

```bash:no-line-numbers
npm install -g @kurrent/gaffer
```

It needs Node.js 22 or later. See [Install](https://gaffer.kurrent.io/getting-started/install/) for other options.

You also need Docker for the [Deploy to KurrentDB](#deploy-to-kurrentdb) section. Until then no database is involved: the projection runs entirely on your machine.

::: tip
Gaffer collects anonymous usage telemetry and checks once a day for a newer release. It tells you so the first time you run it. Set `GAFFER_TELEMETRY_OPTOUT=1` to turn the telemetry off.
:::

::: tip
Every step below is a branch in the [companion project](https://github.com/kurrent-io/projections-tutorial), so you can start anywhere rather than typing along from the top:

```bash:no-line-numbers
git clone https://github.com/kurrent-io/projections-tutorial
cd projections-tutorial
git switch step-03-run
```
:::

## Create a project

In an empty directory, create a project and scaffold your first projection:

```bash:no-line-numbers
gaffer init
gaffer scaffold projections/xbox-one-s-counter.js --engine-version 1 --yes
```

`gaffer init` writes a `gaffer.toml`, the file that lists your projections and the databases you deploy them to. `gaffer scaffold` adds a projection to it and creates the JavaScript file:

```toml
[[projection]]
  name = "xbox-one-s-counter"
  entry = "projections/xbox-one-s-counter.js"
  engine_version = 1
```

Drop the `--yes` and scaffold asks you about each of these instead - which events to read, whether to partition, whether the projection emits, and which engine to use.

The engine matters here. This version of KurrentDB runs V1, so `--engine-version 1` is explicit because gaffer would otherwise pick V2, an experimental parallel-partition engine added in KurrentDB 26.1.

The scaffolded projection is a working skeleton with no logic in it yet:

```javascript
fromAll()
  .when({
    $init() {
      return {};
    },
    // Add your event handlers here
    // EventType(state, event) {
    //   return state;
    // }
  })
```

Two pieces to know:

- **`fromAll()`** is the selector. It reads every event in the database. `fromStream`, `fromStreams` and `fromCategory` narrow that down, and you use one of them later in this tutorial.
- **`.when({...})`** is the handler map. `$init` returns the projection's starting state, and every other key names an event type. A handler receives the current state and the incoming event, and returns the new state.

## Count the consoles

Replace the handler map so the projection counts `ItemAdded` events whose description names an Xbox One S:

```javascript
fromAll()
  .when({
    $init() {
      return { count: 0 };
    },
    ItemAdded(state, event) {
      if (event.body.Description.indexOf("Xbox One S") >= 0) {
        state.count += 1;
      }
      return state;
    },
  })
```

`event.body` is the event's JSON payload, already parsed. Events that are not `ItemAdded` have no handler, so the projection ignores them.

## Run it without a database

A projection is only interesting once events flow through it, and you do not need a running KurrentDB for that. Put some events in a JSON file - `fixtures/carts.json` - where each entry names the stream it belongs to:

```json
[
  {
    "eventType": "ItemAdded",
    "streamId": "shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164",
    "data": "{\"Description\": \"Xbox One S 1TB (Console)\"}"
  },
  {
    "eventType": "ItemAdded",
    "streamId": "shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164",
    "data": "{\"Description\": \"Gears of War 4\"}"
  }
]
```

The companion project's file holds seven such events across four carts. Register it in `gaffer.toml` as a named fixture:

```toml
[[projection]]
  name = "xbox-one-s-counter"
  entry = "projections/xbox-one-s-counter.js"
  engine_version = 1
  fixtures.carts = "fixtures/carts.json"
```

Then run the projection against it:

```bash:no-line-numbers
gaffer dev xbox-one-s-counter --fixture carts
```

Gaffer summarises what it found in your projection, then feeds each event through your handlers and shows the state after every one:

```text:no-line-numbers
xbox-one-s-counter
   Source: $all
   Events: ItemAdded
   Engine: v1
   Quirks: unversioned (matching all KurrentDB quirks)

0@shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164
│  type: ItemAdded
│  data: {"Description": "Xbox One S 1TB (Console)"}
╰ processed
   state: {"count":1}

1@shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164
│  type: ItemAdded
│  data: {"Description": "Gears of War 4"}
╰ processed
   state: {"count":1}

7 events processed

   State: {"count":3}
```

Three of the seven items are Xbox One S consoles, and the second event leaves the count alone because a copy of Gears of War 4 is not a console. Edit the projection and run it again as often as you like: the loop takes seconds and touches no database.

::: tip
The [VS Code extension](https://gaffer.kurrent.io/extension/vs-code/) runs the same thing with breakpoints, so you can step through a handler and inspect the state as it changes.
:::

## Count the items in each cart

The projection so far keeps one number for the whole database. More often you want a number per stream - here, how many items are in each cart.

Add a second projection, `projections/shopping-cart-counter.js`:

```javascript
fromCategory("shoppingCart")
  .foreachStream()
  .when({
    $init() {
      return { count: 0 };
    },
    ItemAdded(state) {
      state.count += 1;
      return state;
    },
  })
```

Two changes matter. `fromCategory("shoppingCart")` reads only the streams whose names start with `shoppingCart-`, and `.foreachStream()` gives each of those streams its own state, so the counters do not merge into one.

Register it the same way, then run it:

```bash:no-line-numbers
gaffer dev shopping-cart-counter --fixture carts
```

This time the result is one state per cart, printed in no particular order:

```text:no-line-numbers
7 events processed

shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164
   state: {"count":2}
shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1165
   state: {"count":2}
shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1166
   state: {"count":2}
shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1167
   state: {"count":1}
```

::: tip
On a server, `fromCategory` reads the `$ce-shoppingCart` stream, which the [`$by_category`](system.md#by-category) system projection maintains. That projection must be running, which the database you start in the next section does for you.
:::

## Deploy to KurrentDB

Start a database. From the companion project:

```bash:no-line-numbers
docker compose up -d
docker compose wait seed
```

The first command starts a single insecure KurrentDB node on `localhost:2113` with projections enabled, alongside a one-shot service that appends the same seven events to it. The second waits for that seeding to finish, so the projections have the same data to work with as they did locally.

Tell gaffer about the database by adding an environment to `gaffer.toml`:

```toml
[env.local]
connection = "kurrentdb://localhost:2113?tls=false"
default = true
```

Then deploy:

```bash:no-line-numbers
gaffer deploy
```

Gaffer works out what needs to change and shows you the plan before writing anything: what it would create or update, with everything already in sync reported as a count rather than listed. Confirming applies it:

```text:no-line-numbers
  ✓ xbox-one-s-counter     created
  ✓ shopping-cart-counter  created

2 created · 0 updated · 0 skipped
```

Deploying is safe to repeat. A projection whose definition has not changed is skipped rather than rewritten, so running `gaffer deploy` twice does nothing the second time. In a script, pass `--yes` to skip the confirmation.

`gaffer status` then lists each projection with its state on the server, when it was last deployed, and whether what is deployed still matches your local files.

Your projections are now running on the server, processing events as they arrive. Ask the server what a projection's state is - give it a moment after deploying, since a projection that has just started may still be working through the events:

```bash:no-line-numbers
curl http://localhost:2113/projection/xbox-one-s-counter/state
```

```json
{"count":3}
```

The same count you saw locally. For the partitioned projection, name the partition you want:

```bash:no-line-numbers
curl "http://localhost:2113/projection/shopping-cart-counter/state?partition=shoppingCart-b989fe21-9469-4017-8d71-9820b8dd1164"
```

```json
{"count":2}
```

::: tip
Polling for state is not the only option. A projection that calls `.outputState()` also publishes its state as events - on `$projections-{projection-name}-result`, or one stream per partition for a partitioned projection - and a client can subscribe to those the way it would any other stream. They are system streams, so a secured server may require additional permissions to read them. See [User-defined projections](custom.md#projections-api) for that and the rest of the API.
:::

## Change it and see the difference

Projections evolve. Suppose you also want to know how many items were added in total, not only the consoles:

```javascript
    $init() {
      return { count: 0, items: 0 };
    },
    ItemAdded(state, event) {
      state.items += 1;
      if (event.body.Description.indexOf("Xbox One S") >= 0) {
        state.count += 1;
      }
      return state;
    },
```

Your local file and the deployed projection now disagree. Ask gaffer what changed:

```bash:no-line-numbers
gaffer diff xbox-one-s-counter
```

```text:no-line-numbers
xbox-one-s-counter
   Query: +2 -1
   Engine version: 1
   Emit: disabled
   Drift: drifted
   Last deploy: 2026-08-12 13:08

 1  1   fromAll()
 2  2     .when({
 3  3       $init() {
 4    -       return { count: 0 };
    4 +       return { count: 0, items: 0 };
 5  5       },
 6  6       ItemAdded(state, event) {
    7 +       state.items += 1;
 7  8         if (event.body.Description.indexOf("Xbox One S") >= 0) {
 8  9           state.count += 1;
 9 10         }
10 11         return state;
11 12       },
12 13     })
```

Run it locally first if you want to check the new logic, then `gaffer deploy` again to update the server.

::: warning
Changing a projection's logic does not change the state it has already built. By default the updated projection carries on from where it left off, so state accumulated by the old code is still there - here, `items` counts from the point you deployed rather than from the first event. Pass `--reset-on-logic-change` to `gaffer deploy` to reprocess from the beginning instead.
:::

## Next steps

- [User-defined projections](custom.md) - the full JavaScript API: selectors, partitioning, emitting events, and the options you can set.
- [System projections](system.md) - the projections KurrentDB ships with, including the `$by_category` projection used above.
- [Gaffer documentation](https://gaffer.kurrent.io) - testing projections, deploying from CI, reviewing deploy history, and rolling back.
- Managing projections from your application: see the projections page for your [client SDK](https://docs.kurrent.io/clients/).
