---
title: Networking
order: 2
---

KurrentDB provides two interfaces:
- HTTP(S) for gRPC communication and REST APIs (node)
- TCP for cluster replication (replication)

Nodes in the cluster replicate with each other using the TCP protocol, but use gRPC for [discovering other cluster nodes](cluster.md#discovering-cluster-members).

For gRPC and HTTP, there's no internal vs external separation of traffic.

## HTTP configuration

HTTP is the primary protocol for KurrentDB. It is used in gRPC communication and HTTP APIs (management, gossip and diagnostics).
The HTTP endpoint always binds to the IP address configured in the `NodeIp` setting (previously `ExtIp` setting).

| Format               | Syntax               |
|:---------------------|:---------------------|
| Command line         | `--node-ip`          |
| YAML                 | `NodeIp`             |
| Environment variable | `KURRENTDB_NODE_IP`  |

When the `NodeIp` setting is not provided, KurrentDB will use the first available non-loopback address. You can also bind HTTP to all available interfaces using `0.0.0.0` as the setting value. If you do that, you'd need to configure the `NodeHostAdvertiseAs` (previously `ExtHostAdvertiseAs`) setting (read more [here](#network-address-translation)), since `0.0.0.0` is not a valid IP address to connect from the outside world.

::: warning
Please note that the `ExtIp` setting has been removed as of version 25.0.0, use the `NodeIp` setting instead.
:::

The default HTTP port is `2113`. Depending on the [security settings](../security/README.md) of the node, it either responds over plain HTTP or via HTTPS. There is no HSTS redirect, so if you try reaching a secure node via HTTP, you can get an empty response.

You can change the HTTP port using the `NodePort` setting (previously `HttpPort` setting) :

| Format               | Syntax                 |
|:---------------------|:-----------------------|
| Command line         | `--node-port`          |
| YAML                 | `NodePort`             |
| Environment variable | `KURRENTDB_NODE_PORT`  |

**Default**: `2113`

::: warning
Please note that the `HttpPort` setting has been removed as of version 25.0.0, use the `NodePort` setting instead.
:::

If your network setup requires any kind of IP address, DNS name and port translation for internal or external communication, you can use available [address translation](#network-address-translation) settings.

### Keep-alive pings

The reliability of the connection between the client application and database is crucial for the stability of the solution. If the network is not stable or has some periodic issues, the client may drop the connection. Stability is essential for the [stream subscriptions](@clients/grpc/subscriptions.md) where a client is listening to database notifications. Having an existing connection open when an app resumes activity allows for the initial gRPC calls to be made quickly, without any delay caused by the reestablished connection.

KurrentDB supports the built-in gRPC mechanism for keeping the connection alive. If the other side does not acknowledge the ping within a certain period, the connection will be closed. Note that pings are only necessary when there's no activity on the connection.

Keepalive pings are enabled by default, with the default interval set to 10 seconds. The default value is based on the [gRPC proposal](https://github.com/grpc/proposal/blob/master/A8-client-side-keepalive.md#extending-for-basic-health-checking) that suggests 10 seconds as the minimum. It's a compromise value to ensure that the connection is open and not making too many redundant network calls.

You can customise the following Keepalive settings:

#### KeepAliveInterval

After a duration of `keepAliveInterval` (in milliseconds), if the server doesn't see any activity, it pings the client to see if the transport is still alive.

| Format               | Syntax                           |
|:---------------------|:---------------------------------|
| Command line         | `--keep-alive-interval`          |
| YAML                 | `KeepAliveInterval`              |
| Environment variable | `KURRENTDB_KEEP_ALIVE_INTERVAL`  |

**Default**: `10000` (ms, 10 sec)

#### KeepAliveTimeout

After having pinged for keepalive check, the server waits for a duration of `keepAliveTimeout` (in milliseconds). If the connection doesn't have any activity even after that, it gets closed.

| Format               | Syntax                          |
|:---------------------|:--------------------------------|
| Command line         | `--keep-alive-timeout`          |
| YAML                 | `KeepAliveTimeout`              |
| Environment variable | `KURRENTDB_KEEP_ALIVE_TIMEOUT`  |

**Default**: `10000` (ms, 10 sec)

As a general rule, we do not recommend putting KurrentDB behind a load balancer. However, if you are using it and want to benefit from the Keepalive feature, then you should make sure if the compatible settings are properly set. Some load balancers may also override the Keepalive settings. Most of them require setting the idle timeout larger/longer than the `keepAliveTimeout`. We suggest checking the load balancer documentation before using Keepalive pings.

### AtomPub

The AtomPub application protocol over HTTP is disabled by default since v20. We plan to deprecate the AtomPub support in the future versions, but we aim to provide a replacement before we finally deprecate AtomPub.

In Kurrent Cloud, the AtomPub protocol is enabled. For self-hosted instances, use the configuration setting to enable AtomPub.

| Format               | Syntax                                 |
|:---------------------|:---------------------------------------|
| Command line         | `--enable-atom-pub-over-http`          |
| YAML                 | `EnableAtomPubOverHttp`                |
| Environment variable | `KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP`  |

**Default**: `false` (AtomPub is disabled)

### HTTP caching

:::tip
This section is about caching static resources for HTTP API. It does not affect the server performance directly and cannot be used with gRPC clients.
:::

Most of the URIs that KurrentDB emits are immutable (including the UI and Atom Feeds).

An Atom feed has a URI that represents an event, e.g., `/streams/foo/0` which represents 'event 0'. The data for event 0 never changes. If this stream is open to public reads, then the URI is set to be 'cachable' for long periods of time.

You can see a similar example in reading a feed. If a stream has 50 events in it, the feed page `20/forward/10` never changes, it will always be events 20-30. Internally, KurrentDB controls serving the right URIs by using `rel` links with feeds (for example `prev`/`next`).

This caching behavior is great for performance in a production environment and we recommended you use it, but in a developer environment it can become confusing.

For example, what happens if you started a database, wrote `/streams/foo/0` and performed a `GET` request? The `GET` request is cachable and now in your cache. Since this is a development environment, you shut down KurrentDB and delete the database. You then restart KurrentDB and append a different event to `/streams/foo/0`. You open your browser and inspect the `/streams/foo/0` stream, and you see the event appended before you deleted the database.

To avoid this during development it's best to run KurrentDB with the `--disable-http-caching` command line option. This disables all caching and solves the issue.

The option can be set as follows:

| Format               | Syntax                            |
|:---------------------|:----------------------------------|
| Command line         | `--disable-http-caching`          |
| YAML                 | `DisableHttpCaching`              |
| Environment variable | `KURRENTDB_DISABLE_HTTP_CACHING`  |

**Default**: `false`, so the HTTP caching is **enabled** by default.

### Kestrel Settings

It's generally not expected that you'll need to update the Kestrel configuration that KurrentDB has set by default, but it's good to know that you can update the following settings if needed.

Kestrel uses the `kestrelsettings.json` configuration file. This file should be located in the [default configuration directory](../configuration/README.md#configuration-files).

#### MaxConcurrentConnections

Sets the maximum number of open connections. See the docs [here](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.maxconcurrentconnections?view=aspnetcore-5.0).

This is configured with `Kestrel.Limits.MaxConcurrentConnections` in the settings file.

#### MaxConcurrentUpgradedConnections

Sets the maximum number of open, upgraded connections. An upgraded connection is one that has been switched from HTTP to another protocol, such as WebSockets. See the docs [here](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.maxconcurrentupgradedconnections?view=aspnetcore-5.0).

This is configured with `Kestrel.Limits.MaxConcurrentUpgradedConnections` in the settings file.

#### Http2 InitialConnectionWindowSize

Sets how much request body data the server is willing to receive and buffer at a time aggregated across all requests (streams) per connection. Note requests are also limited by `KestrelInitialStreamWindowSize`

The value must be greater than or equal to 65,535 and less than 2^31. See the docs [here](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.http2limits.initialconnectionwindowsize?view=aspnetcore-5.0).

This is configured with `Kestrel.Limits.Http2.InitialConnectionWindowSize` in the settings file.

#### Http2 InitialStreamWindowSize

Sets how much request body data the server is willing to receive and buffer at a time per stream. Note connections are also limited by `KestrelInitialConnectionWindowSize`

Value must be greater than or equal to 65,535 and less than 2^31. See the docs [here](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.http2limits.initialstreamwindowsize?view=aspnetcore-5.0).

This is configured with `Kestrel.Limits.Http2.InitialStreamWindowSize` in the settings file.

## Replication protocol

Replication between cluster nodes uses a proprietary TCP-based protocol. Options for configuring the internal replication protocol are described below.

### Interface and port

Internal TCP binds to the IP address specified in the `ReplicationIp` setting (previously `IntIp`). It must be configured if you run a multi-node cluster.

By default, KurrentDB binds its internal networking on the loopback interface only (`127.0.0.1`). You can change this behaviour and tell KurrentDB to listen on a specific internal IP address. To do that set the `ReplicationIp` to `0.0.0.0` or the IP address of the network interface.

| Format               | Syntax                      |
|:---------------------|:----------------------------|
| Command line         | `--replication-ip`          |
| YAML                 | `ReplicationIp`             |
| Environment variable | `KURRENTDB_REPLICATION_IP`  |

**Default**: `127.0.0.1` (loopback).

If you keep this setting to its default value, cluster nodes won't be able to talk to each other.

::: warning
Please note that the `IntIp` setting has been removed as of version 25.0.0, use the `ReplicationIp` setting instead.
:::

By default, KurrentDB uses port `1112` for internal TCP. You can change this by specifying the `ReplicationPort` setting (previously `IntTcpPort` setting).

| Format               | Syntax                        |
|:---------------------|:------------------------------|
| Command line         | `--replication-port`          |
| YAML                 | `ReplicationPort`             |
| Environment variable | `KURRENTDB_REPLICATION_PORT`  |

**Default**: `1112`

::: warning
Please note that the `IntTcpPort` setting has been removed as of version 25.0.0, use the `ReplicationPort` setting instead.
:::

If your network setup requires any kind of IP address, DNS name and port translation for internal communication, you can use available [address translation](#network-address-translation) settings.

## Network address translation

Due to NAT (network address translation), or other reasons a node may not be bound to the address it is reachable from other nodes. For example, the machine has an IP address of `192.168.1.13`, but the node is visible to other nodes as `10.114.12.112`.

Options described below allow you to tell the node that even though it is bound to a given address it should not gossip that address. When returning links over HTTP, KurrentDB will also use the specified addresses instead of physical addresses, so the clients that use HTTP can follow those links.

Another case when you might want to specify the advertised address although there's no address translation involved. When you configure KurrentDB to bind to `0.0.0.0`, it will use the first non-loopback address for gossip. It might or might not be the address you want it to use. Whilst the best way to avoid such a situation is to configure the binding properly using the `NodeIp` and `ReplicationIp` settings, you can also use address translation setting with the correct IP address or DNS name.

Also, even if you specified the `NodeIp` and `ReplicationIp` settings in the configuration, you might still want to override the advertised address if you want to use hostnames and not IP addresses. That might be needed when running a secure cluster with certificates that only contain DNS names of the nodes.

The only place where these settings make any effect is the [gossip](cluster.md#gossip-protocol) endpoint response.

## HTTP translations

By default, a cluster node will advertise itself using `NodeIp` and `NodePort`. You can override the advertised HTTP port using the  `NodePortAdvertiseAs` setting (previously `HttpPortAdvertiseAs` setting).

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Command line         | `--node-port-advertise-as`          |
| YAML                 | `NodePortAdvertiseAs`               |
| Environment variable | `KURRENTDB_NODE_PORT_ADVERTISE_AS`  |

::: warning
Please note that the `HttpPortAdvertiseAs` setting has been removed as of version 25.0.0, use the `NodePortAdvertiseAs` setting instead.
:::

If you want the node to advertise itself using the hostname rather than its IP address, use the `NodeHostAdvertiseAs` setting (previously `ExtHostAdvertiseAs` setting).

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Command line         | `--node-host-advertise-as`          |
| YAML                 | `NodeHostAdvertiseAs`               |
| Environment variable | `KURRENTDB_NODE_HOST_ADVERTISE_AS`  |

::: warning
Please note that the `ExtHostAdvertiseAs` setting has been removed as of version 25.0.0, use the `NodeHostAdvertiseAs` setting instead.
:::

### TCP translations

TCP ports used for replication can be advertised using custom values:

| Format               | Syntax                                         |
|:---------------------|:-----------------------------------------------|
| Command line         | `--replication-tcp-port-advertise-as`          |
| YAML                 | `ReplicationTcpPortAdvertiseAs`                |
| Environment variable | `KURRENTDB_REPLICATION_TCP_PORT_ADVERTISE_AS`  |

::: warning
Please note that the `IntTcpPortAdvertiseAs` setting has been removed as of version 25.0.0, use the `ReplicationTcpPortAdvertiseAs` and `NodeTcpPortAdvertiseAs` setting instead, respectively.
:::

If you want to change how the node TCP address is advertised internally, use the `ReplicationHostAdvertiseAs` setting (previously `IntHostAdvertiseAs` setting). You can use an IP address or a hostname.

| Format               | Syntax                                     |
|:---------------------|:-------------------------------------------|
| Command line         | `--replication-host-advertise-as`          |
| YAML                 | `ReplicationHostAdvertiseAs`               |
| Environment variable | `KURRENTDB_REPLICATION_HOST_ADVERTISE_AS`  |

::: warning
Please note that the `IntHostAdvertiseAs` setting has been removed as of version 25.0.0, use the `ReplicationHostAdvertiseAs` setting instead.
:::

### Advertise to clients

In some cases, the cluster needs to advertise itself to clients using a completely different set of addresses and ports. Usually, you need to do it because addresses and ports configured for the HTTP protocol are not available as-is to the outside world. One of the examples is running a cluster in Docker Compose. In such environment, HTTP uses internal hostnames in the Docker network, which isn't accessible on the host. So, in order to connect to the cluster from the host machine, you need to use `localhost` and translated HTTP ports to reach the cluster nodes.

To configure how the cluster nodes advertise to clients, use the `Advertise<*>ToClient` settings listed below.

Specify the advertised hostname or IP address:

| Format               | Syntax                                   |
|:---------------------|:-----------------------------------------|
| Command line         | `--advertise-host-to-client-as`          |
| YAML                 | `AdvertiseHostToClientAs`                |
| Environment variable | `KURRENTDB_ADVERTISE_HOST_TO_CLIENT_AS`  |

Specify the advertised HTTP(S) port (previously `AdvertiseHttpPortToClientAs` setting):

| Format               | Syntax                                        |
|:---------------------|:----------------------------------------------|
| Command line         | `--advertise-node-port-to-client-as`          |
| YAML                 | `AdvertiseNodePortToClientAs`                 |
| Environment variable | `KURRENTDB_ADVERTISE_NODE_PORT_TO_CLIENT_AS`  |

::: warning
Please note that the `AdvertiseHttpPortToClientAs` setting has been removed as of version 25.0.0, use the `AdvertiseNodePortToClientAs` setting instead.
:::

## Heartbeat timeouts

KurrentDB uses heartbeats over all TCP connections to discover dead clients and nodes. Heartbeat timeouts should not be too short, as short timeouts will produce false positives. At the same time, setting too long timeouts will prevent discovering dead nodes and clients in time.

Each heartbeat has two points of configuration. The first is the _interval;_ this represents how often the system should consider a heartbeat. KurrentDB doesn't send a heartbeat for every interval, but only if it has not heard from a node within the configured interval. In a busy cluster, you may never see any heartbeats.

The second point of configuration is the _timeout_. This determines how long KurrentDB server waits for a client or node to respond to a heartbeat request.

Different environments need different values for these settings. The defaults are likely fine on a LAN. If you experience frequent elections in your environment, you can try to increase both interval and timeout, for example:

- An interval of 5000ms.
- A timeout of 1000ms.

::: tip
If in doubt, choose higher numbers. This adds a small period of time to discover a dead client or node and is better than the alternative, which is false positives.
:::

Replication/Internal TCP heartbeat (between cluster nodes):

| Format               | Syntax                                      |
|:---------------------|:--------------------------------------------|
| Command line         | `--replication-heartbeat-interval`          |
| YAML                 | `ReplicationHeartbeatInterval`              |
| Environment variable | `KURRENTDB_REPLICATION_HEARTBEAT_INTERVAL`  |

**Default**: `700` (ms)

| Format               | Syntax                                     |
|:---------------------|:-------------------------------------------|
| Command line         | `--replication-heartbeat-timeout`          |
| YAML                 | `ReplicationHeartbeatTimeout`              |
| Environment variable | `KURRENTDB_REPLICATION_HEARTBEAT_TIMEOUT`  |

**Default**: `700` (ms)

::: warning
Please note that the `IntTcpHeartbeatInterval` and `IntTcpHeartbeatTimeout` setting have been removed as of version 25.0.0, use the `ReplicationHeartbeatInterval` and `ReplicationHeartbeatTimeout` setting instead, respectively.
:::

### gRPC heartbeats

For the gRPC heartbeats, KurrentDB and its gRPC clients use the protocol feature called _Keepalive ping_. Read more about it on the [HTTP configuration page](#keep-alive-pings).

## Exposing endpoints

If you need to disable some HTTP endpoints on the external HTTP interface, you can change some settings below. It is possible to disable the Admin UI, stats and gossip port to be exposed externally.

You can disable the Admin UI on external HTTP by setting `AdminOnExt` setting to `false`.

| Format               | Syntax                    |
|:---------------------|:--------------------------|
| Command line         | `--admin-on-ext`          |
| YAML                 | `AdminOnExt`              |
| Environment variable | `KURRENTDB_ADMIN_ON_EXT`  |

**Default**: `true`, Admin UI is enabled on the external HTTP.

Exposing the `stats` endpoint externally is required for the Admin UI and can also be useful if you collect stats for an external monitoring tool.

| Format               | Syntax                    |
|:---------------------|:--------------------------|
| Command line         | `--stats-on-ext`          |
| YAML                 | `StatsOnExt`              |
| Environment variable | `KURRENTDB_STATS_ON_EXT`  |

**Default**: `true`, stats endpoint is enabled on the external HTTP.

You can also disable the gossip protocol in the external HTTP interface. If you do that, ensure that the internal interface is properly configured. Also, if you use [gossip with DNS](cluster.md#cluster-with-dns), ensure that the [gossip port](cluster.md#gossip-port) is set to the internal HTTP port.

| Format               | Syntax                     |
|:---------------------|:---------------------------|
| Command line         | `--gossip-on-ext`          |
| YAML                 | `GossipOnExt`              |
| Environment variable | `KURRENTDB_GOSSIP_ON_EXT`  |

**Default**: `true`, gossip is enabled on the external HTTP.

## External TCP

<wbr><Badge type="info" text="License Required" vertical="middle"/>

The TCP client protocol plugin enables client applications based on the external TCP API to run without any changes. This provides developers a mechanism to migrate to gRPC clients. This bridge solution enables development teams to plan for the End Of Life (EOL) of the external TCP API. Past the EOL, the external TCP API will no longer be supported.

### Configuration

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

Sample configuration:

```yaml
TcpPlugin:
  EnableExternalTcp: true
```

Once enabled, the server will log a message similar to the one below:

```
[11212, 1,18:44:34.070,INF] "TcpApi" "25.0.0.1673" plugin enabled.
```

#### Other setting

The following options can be set in the json configuration:

1. `NodeHeartbeatTimeout` - defaults to: 1000
2. `NodeHeartbeatInterval` - defaults to: 2000
3. `NodeTcpPort` - defaults to: 1113
4. `NodeTcpPortAdvertiseAs` - defaults to: 1113

The above default values can be overridden, for example:

```yaml
TcpPlugin:
  EnableExternalTcp: true
  NodeTcpPort: 8113
  NodeTcpPortAdvertiseAs: 8113
  NodeHeartbeatInterval: 2000
  NodeHeartbeatTimeout: 1000
```

### Troubleshooting

#### Plugin doesn't start

The plugin has to be configured to be enabled.
If you see the following log it means the plugin was found but not started:

```
[ 5104, 1,19:03:13.807,INF] "TcpApi" "25.0.0.1673" plugin disabled. "Set 'KurrentDB:TcpPlugin:EnableExternalTcp' to 'true' to enable"
```

When the plugin starts, you should see a log similar to the following:

```
[11212, 1,18:44:34.070,INF] "TcpApi" "25.0.0.1673" plugin enabled.
```
