---
title: "Upgrade guide"
order: 5
---

# Upgrade guide for EventStoreDB 24.10

As of version 24.10, all of our packages are hosted on [Cloudsmith](https://cloudsmith.io/~eventstore/repos/eventstore/packages/). Packages are available for [Debian](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-deb), [RedHat](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-rpm), [Docker](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-docker), and [NuGet](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-nuget).

You can also download the package files for each platform from our [website](https://www.eventstore.com/downloads).

There is no longer a distinction between the open-source (OSS) and commercial versions of EventStoreDB. This unified release is licensed under [ESLv2](https://github.com/EventStore/EventStore/blob/master/LICENSE.md), meaning that anyone can access and use it, but enterprise features are only enabled with a valid license key.

If you have a previous version of EventStoreDB installed through PackageCloud or Chocolatey, please uninstall those versions before installing version 24.10 from Cloudsmith.

## Should you upgrade?

We recommend upgrading if you are interested in any of the [new features](./whatsnew.md) in the 24.10 LTS release.  Also, please note with the release of 24.10, versions 24.2 and 24.6 are no longer supported.  Users of these versions should upgrade to 24.10. 

## Upgrade procedure

You can perform an online rolling upgrade directly to 24.10 from these versions of EventStoreDB:
- 24.6
- 24.2
- 23.10
- 22.10
- 21.10

Follow the upgrade procedure below on each node, starting with a follower node:

1. Stop the node.
2. Uninstall any previous versions of EventStoreDB.
3. Install EventStoreDB 24.10 and update the configuration. If you use licensed features, ensure that you configure a [license key](../quick-start/installation.md#license-keys).
4. Start the node.
5. Wait for the node to become a follower or read-only replica.
6. Repeat the process for the next node.

Upgrading the cluster this way keeps the cluster online and able to service requests. There may still be disruptions to your services during the upgrade, namely:
- Client connections may be disconnected when nodes go offline or elections occur.
- The cluster is less fault-tolerant while a node is offline for an upgrade because the cluster requires a quorum of nodes to be online to service write requests.
- Replicating large amounts of data to a node can have a performance impact on the Leader in the cluster.

::: warning
If you modified the Linux service file to increase the open files limit, those changes will be overridden during the upgrade. You will need to reapply them after the upgrade.
:::

## Upgrading from 24.10-preview1

There have been a few changes to 24.10 since the preview, both in the configuration and the behavior of some new features.

Be aware of the following if upgrading from or tested 24.10.0-preview1.

### Plugins configuration section removed

The configuration for some features was previously nested in a subsection titled `Plugins`. This has been changed, so all configurations are nested in the `EventStore` subsection.

Additionally, these features can now be configured directly in the server main config as well as via JSON or environment variables.

This specifically affects the following features in the preview:

- [License keys](#license-keys)
- [Auto-scavenge](#auto-scavenge)
- [Stream policy authorization](#stream-policy-authorization)
- [Connectors](#connectors)
- [Encryption-at-rest](#encryption-at-rest)

And the following features from versions 23.10 and below:

- [Otel exporter](#otel-exporter-commercial-plugin-configuration-changes)
- [User certificates](#user-certificates-commercial-plugin-configuration-changes)

As an example, if a feature were enabled with a JSON file with a `Plugins` subsection, the JSON would previously have been structured like this:

```json
{
  "EventStore": {
    "Plugins": {
      "Feature_Name": {
        "Feature_Option": "value"
      }
    }
  }
}
```

With the subsection removed, it would look like this:

```json
{
  "EventStore": {
    "Feature_Name": {
      "Feature_Option": "value"
    }
  }
}
```

And can instead be moved to the main config file like this:

```yaml
Feature_Name:
  Feature_Option: "value"
```

Similarly, environment variables with the `PLUGINS` section have been changed. From this:

```bash
EVENTSTORE__PLUGINS__FEATURE_NAME__FEATURE_OPTION="value"
```

To this:

```bash
EVENTSTORE_FEATURE_NAME__FEATURE_OPTION="value"
```

If EventStoreDB detects any configuration in the `Plugins` subsection at startup, it will log a warning:

```
[29364, 1,10:42:37.475,WRN] ClusterVNode    The "Plugins" configuration subsection has been removed. The following settings will be ignored. Please move them out of the "Plugins" subsection and directly into the "EventStore" root.
[29364, 1,10:42:37.476,WRN] ClusterVNode    Ignoring option nested in "Plugins" subsection: EventStore:Plugins:Licensing:LicenseKey
```

Refer to the [configuration guide](../configuration/README.md) for more details about the available configuration mechanisms.

### License keys

The configuration for providing license keys has changed to remove the `Plugins` subsection.

For example, an old JSON configuration file for a license key would have looked like this:

```json
{
  "EventStore": {
    "Plugins": {
      "Licensing": {
        "LicenseKey": "Your key"
      }
    }
  }
}
```

Which would now look like this:

```json
{
  "EventStore": {
    "Licensing": {
      "LicenseKey": "Your key"
    }
  }
}
```

And can be moved to the main config file like this:

```yaml
Licensing:
  LicenseKey: Yourkey
```

Or with the environment variable:

```bash
EVENTSTORE_LICENSING__LICENSE_KEY="Your key"
```

### Auto-scavenge

Auto-scavenge no longer needs to be enabled through the configuration.

Instead, it is automatically enabled by default when a valid license key is provided. It can be disabled with the following configuration:

```yaml
AutoScavenge:
  Enabled: false
```

::: note
EventStoreDB will not run scavenges until a schedule is set via the HTTP endpoint.
:::

Refer to [auto-scavenge](../operations/auto-scavenge.md) for more details about this feature.

### Stream policy authorization

Stream policy authorization can now be enabled across a cluster via the `$authorization-policy-settings` stream. A default policy type may be specified with the following configuration:

```yaml
Authorization:
  DefaultPolicyType: streampolicy
```

Refer to [stream policy authorization](../security/user-authorization.md#stream-policy-authorization) for more details about enabling and configuring this feature.

### Connectors  

#### 1. Commercial License Requirement

The [Kafka](../features/connectors/sinks/kafka.md), [MongoDB](../features/connectors/sinks/mongo.md) and [RabbitMQ](../features/connectors/sinks/rabbitmq.md) sink connectors now require a **commercial license**. Ensure that you configure a valid license key before using these connectors. If you are using these sink connectors, ensure that you configure a [license key](../quick-start/installation.md#license-keys).

#### 2. New Optional Filter Type in Subscription Filters

A new `filterType` configuration has been added to subscription filters, allowing for more precise filtering. Previously, the filter type was inferred, but now you must specify one of the following:  

- `StreamId`  
- `Regex`  
- `Prefix`  
- `JsonPath`  

For example, the following configuration specifies a regex filter:

```json
{
  "subscription:filter:scope": "record",
  "subscription:filter:filterType": "regex",
  "subscription:filter:expression": "^eventType.*"
}
```  

If `filterType` is not specified, the default behavior remains unchanged. For more details, refer to the [Filters](../features/connectors/features.md#filters).  

#### 3. Transformation Function Name

The transformation function must now be named **`transform`**.

Before this change, users could specify a custom function name in their configuration:  

```json
{
  "FunctionName": "customFunctionName"
}
```

This allowed defining the function as:  

```js
function customFunctionName(transformEvent) {
  // your transformation logic
}
```

Now, the function must always be named `transform`:  

```js
function transform(record) {
  // your transformation logic
}
```

#### 4. Direct Record Modification in Transformations

Transformation functions no longer need to return a modified record explicitly. Instead, they can directly modify the record in place.

For example, the following transformation function previously returned a modified record:

```js
function transform(record) {
    let { Value } = record;

    return {
        ...record,
        Value: {
            ...Value,
            SchemaInfo: {
                ...Value.SchemaInfo,
                Subject: 'Vehicle'
            },
            Vehicle: {
                ...Value.Vehicle,
                MakeModel: `${Value.Vehicle.Make} ${Value.Vehicle.Model}`
            }
        }
    }
}
```

With the latest changes, the function can now directly modify the record:

```js
function transform(record) {
  let { make, model } = record.value.vehicle;
  record.schemaInfo.subject = 'Vehicle';
  record.value.vehicle.makemodel = `${make} ${model}`;
}
```

For more details, refer to the [Transformations](../features/connectors/features.md#transformations).  

#### 5. Removed Transformation `ExecutionTimeoutMs` Option

The `ExecutionTimeoutMs` option has been removed from transformation configurations and will be ignored if present.

#### 6. Aliases for `instanceTypeName`

You can now use aliases when defining an `instanceTypeName`.  

Previously you had to specify the full connector type:

```json
{
  "instanceTypeName": "EventStore.Connectors.Http.HttpSink"
}
```

You can now use a pascal case or kebab alias instead:

```json
{
  "instanceTypeName": "HttpSink"
}
```

```json
{
  "instanceTypeName": "http-sink"
}
```

### Encryption-at-rest

Only the configuration for encryption-at-rest has changed since 24.10.0-preview1.

For example, an old JSON configuration file for encryption-at-rest would have looked like this:

```json
{
  "EventStore": {
    "Plugins": {
      "EncryptionAtRest": {
        "Enabled": true,
        "MasterKey": {
          "File": {
            "KeyPath": "/path/to/keys/"
          }
        },
        "Encryption": {
          "AesGcm": {
            "Enabled": true,
            "KeySize": 256
          }
        }
      }
    }
  }
}
```

Which would now look like this:

```json
{
  "EventStore": {
    "EncryptionAtRest": {
      "Enabled": true,
      "MasterKey": {
        "File": {
          "KeyPath": "/path/to/keys/"
        }
      },
      "Encryption": {
        "AesGcm": {
          "Enabled": true,
          "KeySize": 256
        }
      }
    }
  }
}
```

And could be moved to the main config file like this:

```yaml
Transform: aes-gcm

EncryptionAtRest:
  Enabled: true
  MasterKey:
    File:
      KeyPath: /path/to/keys/
  Encryption:
    AesGcm:
      Enabled: true
      KeySize: 256
```

## Breaking changes

### From version 24.6 and earlier

#### Histograms endpoint has been removed

The `/histogram/{name}` endpoint has been removed.

Any tooling that relies on the histogram endpoint will receive a 404 when requesting this endpoint after upgrading.

#### Support for v1 PTables has been removed

Support for extremely old PTables (v1) has been removed.

This will only affect databases created on EventStoreDB version 3.9.0 and before, and which have not upgraded their PTables since EventStoreDB version 3.9.0.

PTables are automatically upgraded when merged or when the PTables are rebuilt. So, if your EventStoreDB has been running for some time on a version greater than 3.9.0, then you are unlikely to be affected by this change.

If 32bit PTables are present, we detect them on startup and exit. If this happens, you can use a version between v3.9.0 and v24.10.0 to upgrade the PTables or rebuild the index.

#### Otel Exporter commercial plugin configuration changes

The configuration for this plugin is now nested in the `EventStore` subsection to ensure consistency with the other plugins. Additionally, this plugin used to be configured via JSON or environment variables, but it can now be configured directly in the server's main configuration.

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
  "EventStore": {
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
  "EventStore": {
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

### From version 23.10 and earlier

#### External TCP API removed

The external TCP API was removed in 24.2.0. This affects external clients using the TCP API and its related configurations.

::: tip
EventStoreDB 24.10 includes [a plugin](../configuration/networking.md#external-tcp) that enables the TCP client protocol. This plugin can only be used with a [license](../quick-start/installation.md#license-keys)
:::

A number of configuration options have been removed as part of this. EventStoreDB will not start by default if any of the following options are present in the database configuration:

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

### From version 22.10 and earlier

The updates to anonymous access described in the [release notes](https://www.eventstore.com/blog/23.10.0-release-notes) have introduced some breaking changes. We have also removed, renamed, and deprecated some options in EventStoreDB.

None of these changes will prevent you from performing an online rolling upgrade of the cluster, but you will need to take them into account before you perform an upgrade.

When upgrading from 22.10 and earlier, you will need to account for the following breaking changes:

#### Clients must be authenticated by default

We have disabled anonymous access to streams by default in this version. This means that read and write requests from clients need to be authenticated.

If you see authentication errors when connecting to EventStoreDB after upgrading, please ensure that you either use default credentials on the connection or provide user credentials with the request itself.

If you want to revert to the old behavior, you can enable the `AllowAnonymousStreamAccess` and `AllowAnonymousEndpointAccess` options in EventStoreDB.
Requests to the HTTP API must be authenticated by default.
Like with anonymous access to streams, anonymous access to the HTTP and gRPC endpoints has been disabled by default. The exceptions are the `/gossip`, `/info`, and `/ping` endpoints.

Any tools or monitoring scripts accessing the HTTP endpoints (e.g., `/stats`) must make authenticated requests to EventStoreDB.

If you want to revert to the old behavior, you can enable the `AllowAnonymousStreamAccess` and `AllowAnonymousEndpointAccess` options in EventStoreDB.

#### PrepareCount and CommitCount options have been removed

We have removed the `PrepareCount` and `CommitCount` options from EventStoreDB. EventStoreDB will fail if these options are present in the config on startup.

These options did not have any effect and can be safely removed from your configuration file if you have them defined.

#### Persistent subscriptions config event has been renamed

We have renamed the event type used to store a persistent subscription configuration from `PersistentConfig1` to `$PersistentConfig`. This event type is a system event, so naming it as such will allow certain filters to exclude it correctly.

If you have any tools or clients relying on this event type, you will need to update them before upgrading.

### From 21.10 and earlier

If you are upgrading from version 21.10 and earlier, then you need to be aware of a breaking change in the TCP proto:

#### Proto2 upgraded to Proto3 (TCP)

The server now uses Proto3 for messages sent over TCP. This affects replication between servers in a cluster.

EventStoreDB nodes on version 22.10 cannot replicate data to version 21.10 and below, but older nodes can still replicate to version 22.10 and above.
Follow the [upgrade procedure](#upgrade-procedure) and ensure that the Leader node is the last node to be upgraded to avoid any issues.

#### Deprecated configuration options

Several options are deprecated and slated for removal in future releases. See the table below for guidance.

| Deprecated Option             | Use Instead                     |
|:------------------------------|:--------------------------------|
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
