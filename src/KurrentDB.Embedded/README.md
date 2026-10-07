# KurrentDB.Embedded

Runs a **single-node KurrentDB server inside your own process**, reachable over a **UNIX domain socket**.

It is the same node the server executable runs — the same `ClusterVNode`, the same subsystems (projections,
secondary indexing, connectors, schema registry, API v2), the same gRPC services. What embedding changes is
the hosting and the way in.

## Why a socket

A connection over the socket is authenticated by the server as the **system account**
(`UnixSocketAuthenticationProvider`), so an embedded client needs no credentials and sends none. The socket
file itself is what grants that access, so it is created inside an owner-only directory and set to `0600`
once it is bound.

The regular .NET client works over it unchanged: `CreateClientSettings()` returns
`KurrentDBClientSettings` whose handler dials the socket instead of resolving and connecting to an address.

## Usage

```csharp
using KurrentDB.Client;
using KurrentDB.Embedded;

await using var db = new EmbeddedKurrentDB(new EmbeddedKurrentDBOptions {
    DataDirectory = "./data"
});

await db.StartAsync();

await using var client = new KurrentDBClient(db.CreateClientSettings());

await client.AppendToStreamAsync("orders-1", StreamState.Any, [
    new EventData(Uuid.NewUuid(), "order-placed", JsonSerializer.SerializeToUtf8Bytes(order))
]);
```

For the generated service clients, `db.CreateChannel()` gives a `GrpcChannel` over the same socket:

```csharp
using var channel = db.CreateChannel();
var streams = new Streams.StreamsClient(channel);
```

`db.Services` is the node's service provider, so in-process callers can skip gRPC entirely and resolve
`ISystemClient`, `IPublisher` and the rest directly.

## What it does not do over the network

- **No replication listener and no gossip.** A single node elects itself, replicates to nobody and resolves
  no seeds, so `ClusterVNode` never opens the internal TCP endpoint.
- **No telemetry.** `TelemetryOptout` is forced on.
- **No licence call.** A single node with no licence key issues itself one rather than asking for one. Set
  `KurrentDB:Licensing:LicenseKey` through `Settings` and it will contact `licensing.kurrent.io` as usual.
- **No log files.** The node logs through the static `Serilog.Log` logger, so a host that has not configured
  Serilog gets a quiet component. Point `Serilog.Log.Logger` at a sink, or use `ConfigureLogging`, to see it.
- **No statistics in the log.** `StatsStorage` is forced to `None`. The server logs a JSON object of system
  statistics every `StatsPeriodSec` and splits that source context off into a file of its own; a host owns its
  own logging configuration, so an embedded node would just be dropping that object into the host's log every
  30 seconds. The statistics are still collected, so `/stats` answers as usual.

## The TCP listener

**`EnableTcpListener` defaults to `true`, and should not stay that way.** It is on so the admin HTTP API,
the Prometheus endpoint and existing HTTP tooling keep working while this finds its shape; an embedded
database should not have to open a port at all. Two things to know while it is on:

- With `Insecure` (the default), the port has no TLS and no authentication. It binds to loopback only, but
  anything on the machine that can reach loopback has administrator access to the database.
- Turning it off takes the HTTP endpoints with it — anything that reaches the node over HTTP rather than
  over the socket stops working.

## No Blazor UI

The admin HTTP API is here, as on a normal node. The Blazor UI is not, and this library deliberately does
not reference the server executable that carries it — it takes `KurrentDB.Hosting`, which assembles the
same node with the same plugins and subsystems but none of the UI. That keeps MudBlazor, BlazorMonaco and
the Razor runtime out of every application that embeds a database.

It would not have worked anyway: `MapStaticAssets` resolves the UI's assets from a manifest named after the
**host application** — `{ApplicationName}.staticwebassets.endpoints.json`, beside the host's assemblies —
and a project that merely references KurrentDB does not produce one.

## Options

| Option | Default | |
|---|---|---|
| `DataDirectory` | *required* | Where the database is written. Created owner-only if absent. |
| `IndexDirectory` | `<DataDirectory>/index` | |
| `EnableTcpListener` | `true` | See above. |
| `TcpListenerIp` / `TcpListenerPort` | `127.0.0.1` / `2113` | |
| `RunProjections` | `true` | Runs system projections and starts the standard ones. |
| `Insecure` | `true` | No TLS, no authentication, no authorization. |
| `StartupTimeout` | 1 minute | How long `StartAsync` waits for the node to report ready. |
| `Settings` | empty | Any server setting, keyed flat: `KurrentDB:ChunkSize`. Plugins nest: `KurrentDB:Licensing:LicenseKey`. |
| `ConfigureServices` | – | Add or replace DI registrations after the node has registered its own. |
| `ConfigureLogging` | – | Change where the node logs. |

### Where the socket lives

`<DataDirectory>/kurrent.sock`. The node picks the path, exactly as it does when the server runs it — the
database directory is the data directory, so the socket sits inside the owner-only directory that protects
it, and anything that knows where a KurrentDB socket lives still knows. It is not configurable, and
`UnixSocketPath` reads it back off the node once `StartAsync` has returned.

The node declines to open a socket at all in two cases, each of which would leave an embedded database
with no way in, so each is an error rather than a warning: `KurrentDB:Database:MemDb` set through
`Settings`, which the constructor rejects, and an operating system without UNIX domain sockets, which
`StartAsync` reports.

## Lifecycle

`new EmbeddedKurrentDB(...)` works out how the node will be configured and prepares its directory. It opens
nothing: the database files are opened by `StartAsync()`, which returns once the node reports itself ready.
`DisposeAsync()` stops the node and releases the exclusive lock on the database directory.

The constructor refuses a configuration the node could not be built from: `InvalidConfigurationException`
for a setting the server would not start with either, and `InvalidOperationException` for an in-memory
database. `StartAsync` refuses what is only knowable as it starts — a 32-bit process, or an unrecognised
setting when `AllowUnknownOptions` has been turned off through `Settings` — with `InvalidOperationException`
naming the reason.

A database runs once. The node and the web host it runs in are both single-use, so an instance that has been
stopped cannot be started again; construct another one on the same data directory instead. `StartAsync`,
`StopAsync` and `DisposeAsync` take a single lock between them, so they can be called from anywhere,
including the thread pool, and from more than one thread at a time.

## See also

- [`KurrentDB.Embedded.Sample`](../KurrentDB.Embedded.Sample) — a console host that starts a node and then
  works through appends (single stream, and multi-stream atomically via `MultiStreamAppendAsync`), stream
  and `$all` reads in both directions, `$all` subscriptions with server-side regex and stream-prefix
  filters, a single-stream subscription, a soft delete, `$maxAge` metadata, and an `$idx-et-*` event type
  index read.
- [`KurrentDB.Testing.ClusterVNodeApp`](../KurrentDB.Testing.ClusterVNodeApp) — the equivalent harness for
  integration tests, which listens on a TCP port instead.
