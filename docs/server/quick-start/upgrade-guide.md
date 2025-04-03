---
title: "Upgrade guide"
order: 5
---

# Upgrade guide for KurrentDB 25.0

Event Store – the company and the product – are rebranding as Kurrent.

As part of this rebrand, EventStoreDB has been renamed to KurrentDB, with the first release of KurrentDB being version 25.0.

Read more about the rebrand in the [rebrand FAQ](https://www.kurrent.io/blog/kurrent-re-brand-faq).

Packages for KurrentDB are still hosted on [Cloudsmith](https://cloudsmith.io/~eventstore), in the following repositories:

* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) packages.
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) packages.
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview) containing non-production preview packages.

Packages are available for [Debian](./installation.md#debian-packages), [RedHat](./installation.md#redhat-packages), [Docker](./installation.md#docker), and [NuGet](./installation.md#nuget).

## Should you upgrade?

KurrentDB 25.0 is a short term support (STS) feature release and will be supported until the next major or minor release of KurrentDB.

Upgrade to this version if you want to use the new archiving feature, or want to prepare for some of the changes caused by the rebrand from EventStoreDB to KurrentDB.

## Upgrade procedure

You can perform an online rolling upgrade directly to KurrentDB 25.0 from these versions of EventStoreDB:
- 24.10
- 23.10
- 22.10

Follow the upgrade procedure below on each node, starting with a follower node:

1. Stop the node.
1. Uninstall any previous versions of EventStoreDB.
1. Install KurrentDB 25.0 and update the configuration. If you use licensed features, ensure that you configure a [license key](../quick-start/installation.md#license-keys).
1. Start the node.
1. Wait for the node to become a follower or read-only replica.
1. Repeat the process for the next node.

Upgrading the cluster this way keeps the cluster online and able to service requests. There may still be disruptions to your services during the upgrade, namely:
- Client connections may be disconnected when nodes go offline or elections occur.
- The cluster is less fault-tolerant while a node is offline for an upgrade because the cluster requires a quorum of nodes to be online to service write requests.
- Replicating large amounts of data to a node can have a performance impact on the Leader in the cluster.

::: warning
If you modified the Linux service file to increase the open files limit, those changes will be overridden during the upgrade. You will need to reapply them after the upgrade.
:::

## Breaking changes

### File and location changes when upgrading from EventStoreDB

You will need to take the following changes into account when upgrading from EventStoreDB:

#### On Windows

1. The executable `EventStore.ClusterNode.exe` has been renamed to `KurrentDB.exe`.
1. The test client executable `EventStore.TestClient.exe` has been renamed to `KurrentDB.TestClient.exe`.

#### On Linux

1. The `eventstore` service has been renamed to `kurrentdb`.
1. The `eventstored` executable has been renamed to `kurrentd`.
1. The `eventstore` user has been renamed to `kurrent`.
1. The default locations have changed from `eventstore` to `kurrentdb`:

| Old location                      | New location                    | Description               |
| --------------------------------- | ------------------------------- | ------------------------- |
| `/etc/eventstore/eventstore.conf` | `/etc/kurrentdb/kurrentdb.conf` | Default config file       |
| `/usr/bin/eventstored`            | `/usr/bin/kurrentd`             | Symlink to the executable |
| `/var/lib/eventstore/`            | `/var/lib/kurrentdb/ `          | Default data directory    |
| `/var/log/eventstore/`            | `/var/log/kurrentdb/`           | Default log directory     |
| `/usr/share/eventstore/`          | `/usr/share/kurrentdb/`         | Installation directory    |

If the following EventStore locations exist and the KurrentDB locations do not, KurrentDB will use the EventStore locations instead:

- The default config file `/etc/kurrentdb/kurrentdb.conf` -> `/etc/eventstore/eventstore.conf`
- The default data directory `/var/lib/kurrentdb/` -> `/var/lib/eventstore/`
- The default log directory `/var/log/kurrentdb/` -> `/var/log/eventstore/`

If you install KurrentDB through a package manager, it will create a default configuration file at `/etc/kurrentdb/kurrentdb.conf` for you and therefore won't fall back to the EventStore config file. You will need to copy any configuration over from `eventstore.conf` to `kurrentdb.conf`.

::: warning
If you are running KurrentDB as a service, you will need to grant the `kurrent` user access to any data, logs, or configuration directories that the `eventstore` user had access to.
:::

### Log Levels supported in logconfig.json

The log levels specified in `logconfig.json` must now be Microsoft levels rather than Serilog levels.

If you have customized your `logconfig.json` you will need to change `Fatal` to `Critical` and `Verbose` to `Trace`.
[#4837](https://github.com/kurrent-io/EventStore/pull/4837)

### Metrics name changes

::: info
The old EventStore metric names can still be used by changing the two meter names in `metricsconfig.json` to have `EventStore` prefixes:

```diff
  "Meters": [
-    "KurrentDB.Core",
-    "KurrentDB.Projections.Core"
+    "EventStore.Core",
+    "EventStore.Projections.Core"
  ],
```

However, this functionality will eventually be removed in a future release.

If you are using the Open Telemetry Collector, you may also need to set `add_metric_suffixes` to `false` in its configuration file:

```
exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
    add_metric_suffixes: false
```
:::

All of the `eventstore` prefixes have been changed to `kurrentdb`

| Old name        | New name      |
| --------------- | ------------- |
| `eventstore_*`  | `kurrentdb_*` |

The following metric names have had `_total` appended to the end when exposed in Prometheus format on the `/metrics` endpoint to match the spec:

| Old name                                    | New name                                          |
| ------------------------------------------- | ------------------------------------------------- |
| `eventstore_cache_hits_misses`              | `kurrentdb_cache_hits_misses_total`               |
| `eventstore_disk_io_bytes`                  | `kurrentdb_disk_io_bytes_total`                   |
| `eventstore_disk_io_operations`             | `kurrentdb_disk_io_operations_total`              |
| `eventstore_elections_count`                | `kurrentdb_elections_count_total`                 |
| `eventstore_gc_collection_count`            | `kurrentdb_gc_collection_count_total`             |
| `eventstore_incoming_grpc_calls`            | `kurrentdb_incoming_grpc_calls_total`             |
| `eventstore_io_bytes`                       | `kurrentdb_io_bytes_total`                        |
| `eventstore_io_events`                      | `kurrentdb_io_events_total`                       |
| `eventstore_proc_contention_count`          | `kurrentdb_proc_contention_count_total`           |
| `eventstore_proc_exception_count`           | `kurrentdb_proc_exception_count_total`            |
| `eventstore_queue_busy_seconds`             | `kurrentdb_queue_busy_seconds_total`              |
| `eventstore_persistent_sub_items_processed` | `kurrentdb_persistent_sub_items_processed_total`  |

The following metric names have changed generally

| Old name                        | New name                                |
| ------------------------------- | --------------------------------------- |
| `eventstore_gc_total_allocated` | `kurrentdb_gc_allocated_bytes_total`    |
| `eventstore_proc_up_time`       | `kurrentdb_proc_up_time_seconds_total`  |

### Removed configuration options

A number of configuration options have been removed in 25.0. KurrentDB will not start by default if any of these options are present in the database configuration.

The following options were renamed in EventStoreDB version 23.10. KurrentDB will no longer start if the deprecated option is present in the database configuration:

| Deprecated Option             | Use Instead                     |
| ----------------------------- | ------------------------------- |
| `ExtIp`                       | `NodeIp`                        |
| `ExtPort`                     | `NodePort`                      |
| `HttpPortAdvertiseAs`         | `NodePortAdvertiseAs`           |
| `ExtHostAdvertiseAs`          | `NodeHostAdvertiseAs`           |
| `AdvertiseHttpPortToClientAs` | `AdvertiseNodePortToClientAs`   |
| `IntIp`                       | `ReplicationIp`                 |
| `IntTcpPort`                  | `ReplicationPort`               |
| `IntTcpPortAdvertiseAs`       | `ReplicationTcpPortAdvertiseAs` |
| `IntHostAdvertiseAs`          | `ReplicationHostAdvertiseAs`    |
| `IntTcpHeartbeatTimeout`      | `ReplicationHeartbeatTimeout`   |
| `IntTcpHeartbeatInterval`     | `ReplicationHeartbeatInterval`  |

The following deprecated options were removed as they had no effect:

- `AlwaysKeepScavenged`
- `GossipOnSingleNode`
- `DisableInternalTcpTls`
- `OptimizeIndexMerge`

### New OAuth redirect uri

The new embedded web UI requires a new redirect uri in order to work with the OAuth plugin.

If you use the OAuth plugin and the web UI, you will need to allow the `/signin-oidc` redirect uri in your identity server configuration in addition to the `/oauth/callback` uri.

For example, if you had the following redirect uris configured in your identity server:

```json
"RedirectUris": [
  "https://localhost:2113/oauth/callback",
  "https://127.0.0.1:2113/oauth/callback"
]
```

Then you would need to update it to this:

```json
"RedirectUris": [
  "https://localhost:2113/signin-oidc",
  "https://127.0.0.1:2113/signin-oidc",
  "https://localhost:2113/oauth/callback",
  "https://127.0.0.1:2113/oauth/callback"
]
```

### From v24.6 and earlier

#### Histograms endpoint has been removed

The `/histogram/{name}` endpoint has been removed.

Any tooling that relies on the histogram endpoint will receive a 404 when requesting this endpoint after upgrading.

#### Support for v1 PTables has been removed

Support for extremely old PTables (v1) has been removed.

This will only affect databases created on EventStoreDB version 3.9.0 and before, and which have not upgraded their PTables since EventStoreDB version 3.9.0.

PTables are automatically upgraded when merged or when the PTables are rebuilt. So, if your EventStoreDB has been running for some time on a version greater than 3.9.0, then you are unlikely to be affected by this change.

If 32bit PTables are present, we detect them on startup and exit. If this happens, you can use a version between v3.9.0 and v24.10.0 to upgrade the PTables or rebuild the index.

#### Otel Exporter commercial plugin configuration changes

The configuration for this plugin is now nested in the `KurrentDB` subsection to ensure consistency with the other plugins. Additionally, this plugin used to be configured via JSON or environment variables, but it can now be configured directly in the server's main configuration.

For example, an old JSON configuration file could look like this:

```json
{
  "OpenTelemetry": {
    "Otlp": {
      "Endpoint": "http://localhost:4317"
    }
  }
}
```

Which would now look like this:

```json
{
  "KurrentDB": {
    "OpenTelemetry": {
      "Otlp": {
        "Endpoint": "http://localhost:4317"
      }
    }
  }
}
```

And can instead be moved to the main config file like this:

```yaml
OpenTelemetry:
  Otlp:
    Endpoint: "http://localhost:4317"
```

#### User Certificates commercial plugin configuration changes

The configuration for this plugin used to be nested in a subsection titled `Plugins`. This is no longer the case. Additionally, this plugin used to be configured via JSON or environment variables, but it can now be configured directly in the server's main configuration.

For example, an old JSON configuration file could look like this:

```json
{
  "EventStore": {
    "Plugins": {
      "UserCertificates": {
        "Enabled": true
      }
    }
  }
}
```

Which would now look like this:

```json
{
  "KurrentDB": {
    "UserCertificates": {
      "Enabled": true
    }
  }
}
```

And can instead be moved to the main config file like this:

```yaml
UserCertificates:
  Enabled: true
```

### From v23.10 and earlier

#### External TCP API removed

The external TCP API was removed in EventStoreDB 24.2.0. This affects external clients using the TCP API and its related configurations.

::: tip
KurrentDB 25.0 includes [a plugin](../configuration/networking.md#external-tcp) that enables the TCP client protocol. This plugin can only be used with a [license](../quick-start/installation.md#license-keys)
:::

A number of configuration options have been removed as part of this. KurrentDB will not start by default if any of the following options are present in the database configuration:

- `AdvertiseTcpPortToClientAs`
- `DisableExternalTcpTls`
- `EnableExternalTcp`
- `ExtHostAdvertiseAs`
- `ExtTcpHeartbeatInterval`
- `ExtTcpHeartbeatTimeout`
- `ExtTcpPort`
- `ExtTcpPortAdvertiseAs`
- `NodeHeartbeatInterval`
- `NodeHeartbeatTimeout`
- `NodeTcpPort`
- `NodeTcpPortAdvertiseAs`

### From v22.10 and earlier

The updates to anonymous access described in the [release notes](https://www.eventstore.com/blog/23.10.0-release-notes) have introduced some breaking changes. We have also removed, renamed, and deprecated some options in KurrentDB.

None of these changes will prevent you from performing an online rolling upgrade of the cluster, but you will need to take them into account before you perform an upgrade.

When upgrading from 22.10 and earlier, you will need to account for the following breaking changes:

#### Clients must be authenticated by default

We have disabled anonymous access to streams by default. This means that read and write requests from clients need to be authenticated.

If you see authentication errors when connecting to KurrentDB after upgrading, please ensure that you either use default credentials on the connection or provide user credentials with the request itself.

If you want to revert to the old behavior, you can enable the `AllowAnonymousStreamAccess` and `AllowAnonymousEndpointAccess` options in KurrentDB.

#### Requests to the HTTP API must be authenticated by default

Like with anonymous access to streams, anonymous access to the HTTP and gRPC endpoints has been disabled by default. The exceptions are the `/gossip`, `/info`, and `/ping` endpoints.

Any tools or monitoring scripts accessing the HTTP endpoints (e.g., `/stats`) must make authenticated requests to KurrentDB.

If you want to revert to the old behavior, you can enable the `AllowAnonymousStreamAccess` and `AllowAnonymousEndpointAccess` options in KurrentDB.

#### PrepareCount and CommitCount options have been removed

We have removed the `PrepareCount` and `CommitCount` options from KurrentDB. KurrentDB will fail if these options are present in the config on startup.

These options did not have any effect and can be safely removed from your configuration file if you have them defined.

#### Persistent subscriptions config event has been renamed

We have renamed the event type used to store a persistent subscription configuration from `PersistentConfig1` to `$PersistentConfig`. This event type is a system event, so naming it as such will allow certain filters to exclude it correctly.

If you have any tools or clients relying on this event type, you will need to update them before upgrading.

## Deprecations

### Configuration sections and prefixes

The `EventStore` configuration section and configuration root has been renamed to `KurrentDB`.

The `EVENTSTORE_` environment variable prefix has been changed to `KURRENTDB_`

### Custom HTTP content types

The `vnd.eventstore.*` content types have been renamed to `vnd.kurrent.*`:

| Deprecated                                      | Use instead                                   |
| ----------------------------------------------- | --------------------------------------------- |
| `application/vnd.eventstore.atom+json`          | `application/vnd.kurrent.atom+json`           |
| `application/vnd.eventstore.event+json`         | `application/vnd.kurrent.event+json`          |
| `application/vnd.eventstore.events+json`        | `application/vnd.kurrent.events+json`         |
| `application/vnd.eventstore.streamdesc+json`    | `application/vnd.kurrent.streamdesc+json`     |
| `application/vnd.eventstore.competingatom+json` | `application/vnd.kurrent.competingatom+json`  |

The `application/vnd.eventstore.atomsvc+json` content type has been removed and replaced with `application/vnd.kurrent.atomsvc+json`.

Xml content types are unchanged.

### Custom HTTP headers

The `ES-*` HTTP headers have been renamed to `Kurrent-*`.

Deprecated headers accepted by the server:

| Deprecated            | Use instead               |
| --------------------- | ------------------------- |
| `ES-ExpectedVersion`  | `Kurrent-ExpectedVersion` |
| `ES-RequireLeader`    | `Kurrent-RequireLeader`   |
| `ES-RequireMaster`    | `Kurrent-RequireLeader`   |
| `ES-ResolveLinkTos`   | `Kurrent-ResolveLinkTos`  |
| `ES-LongPoll`         | `Kurrent-LongPoll`        |
| `ES-TrustedAuth`      | `Kurrent-TrustedAuth`     |
| `ES-HardDelete`       | `Kurrent-HardDelete`      |
| `ES-EventId`          | `Kurrent-EventId`         |
| `ES-EventType`        | `Kurrent-EventType`       |

Deprecated headers provided by the server in certain responses:

| Deprecated          | Use instead               |
| ------------------- | ------------------------- |
| `ES-Position`       | `Kurrent-Position`        |
| `ES-CurrentVersion` | `Kurrent-CurrentVersion`  |