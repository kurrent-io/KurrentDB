---
title: Kontrol Plane
order: 5
---

# Kontrol Plane

By default, a KurrentDB cluster elects a leader using an algorithm based on Paxos.

In 26.2, we have introduced an alternative: The `Kontrol Plane`. The Kontrol Plane forms its own cluster using the Raft algorithm and then _appoints_ the database leader. At the moment the Kontrol Plane nodes run in the same processes as the regular database nodes, but in the future they will be deployable separately.

Enabling the Kontrol Plane does not affect the clients, the gossip or replication mechanisms, or the log format, so you can enable it with minimal disruption, and can disable it again afterwards. In 26.2 enabling or disabling the Kontrol Plane requires downtime as the nodes restart. Re-enabling it starts a fresh Kontrol Plane, see [Storage](#storage).

The Kontrol Plane is a significant step in our strategy to support multiple databases and also to store distributed but ephemeral or mutable data such as distributed leases and checkpoints.

## Turning it on

The role of a node is controlled with two flags: `IsDataPlaneNode` and `IsKontrolPlaneNode`.

To enable Kontrol Plane on a cluster, enable `IsDataPlaneNode` on every node, enable `IsKontrolPlaneNode` on every node that is not a read-only replica, give each Kontrol Plane node the addresses of the others so they can find each other, give each read-only replica the addresses of the Kontrol Plane nodes, and restart the nodes.

Kontrol Plane nodes have a section for the Kontrol Plane status in the embedded UI.

When `IsDataPlaneNode` and `IsKontrolPlaneNode` are both `false`, which is the default, the node will not use the Kontrol Plane at all and will use the old elections mechanism. Use of Kontrol Plane or old elections must be consistent across the cluster.

| Option               | Command line              | Environment variable              |
|:---------------------|:--------------------------|:----------------------------------|
| `IsKontrolPlaneNode` | `--is-kontrol-plane-node` | `KURRENTDB_IS_KONTROL_PLANE_NODE` |
| `IsDataPlaneNode`    | `--is-data-plane-node`    | `KURRENTDB_IS_DATA_PLANE_NODE`    |

**Default**: `false` for both.

## Networking

| Option                      | Command line                     | Default                          |
|:----------------------------|:---------------------------------|:---------------------------------|
| `KontrollerPort`            | `--kontroller-port`              | `3113`                           |
| `KontrollerPortAdvertiseAs` | `--kontroller-port-advertise-as` | `KontrollerPort`                 |
| `KontrollerHostAdvertiseAs` | `--kontroller-host-advertise-as` | The advertised HTTP host         |

The Kontroller listens on `NodeIp:KontrollerPort` and tells its peers to reach it on
`KontrollerHostAdvertiseAs:KontrollerPortAdvertiseAs`.

::: warning
`KontrollerPort` is considered a private port. When TLS is used, connections to it are authenticated
by client certificate. If TLS is disabled with `--insecure` or `--disable-tls` then connections
to it are not authenticated.
:::

There are two seeds. They are generally different ports on the same hosts.

### KontrolPlaneBootstrapSeed

This is used by Kontrol Plane nodes to discover each other on the first start up.
Populate this for Kontrol Plane nodes in multi-node clusters.
Populate it with the host names and `KontrollerPort`s of the other Kontrol Plane nodes.

| Format               | Syntax                                   |
|:---------------------|:-----------------------------------------|
| Command line         | `--kontrol-plane-bootstrap-seed`         |
| YAML                 | `KontrolPlaneBootstrapSeed`              |
| Environment variable | `KURRENTDB_KONTROL_PLANE_BOOTSTRAP_SEED` |

**Default**: empty.

Example:

```
KontrolPlaneBootstrapSeed:
  - node2.kurrentdb.example.com:3113
  - node3.kurrentdb.example.com:3113
```

### KontrolPlaneApiSeed

This is used by Data Plane nodes that are not Kontrol Plane nodes to discover the Kontrol Plane on start.
Populate this for nodes in multi-node clusters that are only Data Plane nodes and not Kontrol Plane nodes, which today means read-only replicas.
Populate it with the host names and regular HTTP ports (usually 2113) of the Kontrol Plane nodes.

| Format               | Syntax                             |
|:---------------------|:-----------------------------------|
| Command line         | `--kontrol-plane-api-seed`         |
| YAML                 | `KontrolPlaneApiSeed`              |
| Environment variable | `KURRENTDB_KONTROL_PLANE_API_SEED` |

**Default**: empty.

Example:

```
KontrolPlaneApiSeed:
  - node1.kurrentdb.example.com:2113
  - node2.kurrentdb.example.com:2113
  - node3.kurrentdb.example.com:2113
```

## Timeouts

| Option                               | Command line                                | Default |
|:-------------------------------------|:--------------------------------------------|:--------|
| `KontrolPlaneLowerElectionTimeoutMs` | `--kontrol-plane-lower-election-timeout-ms` | `700`   |
| `KontrolPlaneUpperElectionTimeoutMs` | `--kontrol-plane-upper-election-timeout-ms` | `1000`  |
| `KontrolPlaneAppointmentTimeoutMs`   | `--kontrol-plane-appointment-timeout-ms`    | `1000`  |

The two election timeouts bound the Kontrol Plane's own Raft elections. They have nothing to do with database
leadership.

`KontrolPlaneAppointmentTimeoutMs` controls database failover. An appointed database leader
renews its appointment at half this interval; if the Kontrol Plane sees no renewal within it, it
appoints someone else.

## Storage

The Kontroller keeps its Raft log, its snapshots and its membership under `<db>/kontroller`.

A node started **without** `IsKontrolPlaneNode` that finds Kontrol Plane state at that path renames
it to `kontroller-retired-<timestamp>` and logs a warning. Stale Kontrol Plane state must not be
resumed, because it can settle on an epoch that a node outside the answering majority has already
written. The directory is retired rather than deleted so it remains recoverable, but turning the
Kontrol Plane back on afterwards starts a fresh Kontrol Plane.

## Security

The Kontroller's Raft transport reuses the node's own certificate and the cluster's trust settings —
there is nothing extra to configure.

Note that the `KontrollerPort` is a private port and is not authenticated when TLS is disabled, see
[Networking](#networking).
