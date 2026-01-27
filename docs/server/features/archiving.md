---
order: 7
---

# Archiving

## Introducing Archiving

KurrentDB 25.0 introduced the initial release of Archiving: a new major feature to reduce costs and increase scalability of a KurrentDB cluster.

Future releases of KurrentDB will build on and improve this feature.

A [license key](../quick-start/installation.md#license-keys) is required to use this feature.

## Motivation

KurrentDB databases can become very large. Typical KurrentDB deployments require low latency, high throughput access to the data, and so employ large, expensive volumes attached to each node in the cluster. The size of the database can be controlled by deleting data, but only if that data is no longer needed. Often, a large proportion of the data can become old enough that, although it is still required for occasional reads, it is not read frequently, and need not be read quickly. Until now, this 'cold' data has necessarily been stored on the same volumes as the hot data, taking up space and adding to the expense of running a high performance cluster.

With the new Archiving feature, data is uploaded to cheaper storage such as Amazon S3 and then can be removed from the volumes attached to the cluster nodes. The volumes can be correspondingly smaller and cheaper. The nodes are all able to read the archive, and when a read request from a client requires data that is stored in the archive, the node retrieves that data from the archive transparently to the client.

The extra copy of the data in S3 also acts as a backup that is kept up to date as each chunk of data is written to the log.

## Operation

### Summary

- Data written to the database is replicated to all nodes in the deployment as normal.
- A designated _Archiver Node_ is responsible for uploading chunk files into the _archive_.
- Nodes can then remove chunks from their local volumes to save space according to a _retention policy_.
- Read requests read transparently through to the archive as necessary.

::: warning
Read requests that read the archive will have comparatively high latency and at the moment can cause other reads to be queued.
:::

### Populating the Archive

- A designated _Archiver Node_ uploads complete chunk files to the archive once they are committed to the database. The contents of each chunk file are the same as they were locally, except that merged chunks are unmerged for upload.
- The Archiver Node stores an archive checkpoint in the archive indicating how much of the log has been uploaded to the archive.
- The Archiver Node is also a _Read-only Replica_ and does not participate in cluster elections/replication criteria. At the moment Read-only replicas can only be used in 3+ node clusters, and not single node deployments.
- Only chunk files are uploaded. PTables, scavenge.db, etc remain local to each node.

### Removal of data from node volumes

- All nodes can delete chunks from their local volumes.
- The removal is performed during the Scavenge operation.
- Chunks are removed only after they have been uploaded to the archive.
- Chunks are removed only after they no longer meet the simple user defined retention policy.

### Backup/restore considerations

- A backup taken from one node can generally be restored to any other node, but this is not the case with the Archiver Node. The Archiver Node must be restored from a backup that was taken from the Archiver Node itself. Scavenging the archive is not yet implemented, but once it is then if the Archiver node were to be restored from a backup taken from a different node then there would be a risk that the Archiver Node will not completely scavenge the archive. A scavenge with `threshold = -1` would need to be run to restore normal operation.

- When a node starts up, it checks to see if the archive has newer data than it has locally, and if so downloads that data from the archive.
  - This prevents the cluster from ever diverging from the content of the archive, even if multiple nodes are restored from backup simultaneously.
  - When restoring an old backup there may be a lot of data to download and store locally. In such cases it is preferable to create a new backup from another node and use that to restore (but, as described above, do not restore the Archiver Node from a backup of another node).

## Configuration

Sample configuration:

The following settings are required on all nodes (including the Archiver Node) to enable archiving:

```yaml
Licensing:
  LicenseKey: <your key>

Archive:
  Enabled: true

  RetainAtLeast:
    Days: 30
    LogicalBytes: 500000000

  StorageType: S3 # or GCP / Azure (examples below) 
  S3:
    Region: eu-west-1
    Bucket: kurrentdb-cluster-123-archive
```

::: warning
Do not use the same archive bucket for multiple clusters, and do not run more than one Archiver node in a single cluster.
:::

Additionally, this must be placed on the Archiver Node:

```yaml
ReadOnlyReplica: true
Archiver: true
```

The Archiver Node is a read-only replica and does not participate in quorum activities. It must be a separate node to the main cluster nodes. e.g. If you have a three node cluster, you will need a fourth node to be the archiver. If you already have a read-only replica as a forth node then it is possible to use it as the Archiver Node.

`RetainAtLeast` is the retention policy for what to retain in the local volume for each node. It does not affect which chunks are uploaded to the archive by the Archiver Node (which will upload all completed committed chunks). If a chunk contains any data less than `RetainAtLeast:Days` old, then it will not be removed locally. If a chunk contains any data that is within `RetainAtLeast:LogicalBytes` of the tail of the log (strictly: the `scavenge point` of the current scavenge) then it will not be removed locally. The metrics described below can be useful to determine sensible values for the retention policy.

On startup, up to `MaxMemTableSize` events can be read from the log. It is recommended to keep at least this much data locally for faster startup.

`StorageType` must be set to `S3`, `GCP` or `Azure`. Other cloud providers may be supported in the future, please contact us if you are interested.

### Amazon S3 Configuration
_Example:_  
```yaml
  StorageType: S3
  S3:
    Region: eu-west-1
    Bucket: kurrentdb-cluster-123-archive
```

The KurrentDB nodes authenticate with S3 by looking for credentials from the standard providers. Please see the documentation for [S3 in general](https://docs.aws.amazon.com/sdkref/latest/guide/standardized-credentials.html) and [.NET in particular](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/creds-assign.html).

### Google Cloud Platform Configuration
_Example:_  
```yaml
  StorageType: GCP
  GCP:
    Bucket: kurrentdb-cluster-123-archive
```

The KurrentDB nodes authenticate with Google Cloud using _Application Default Credentials_. Please see the documentation for [GCP in general](https://cloud.google.com/docs/authentication/application-default-credentials) and [.NET in particular](https://cloud.google.com/dotnet/docs/setup#authn).

### Microsoft Azure Configuration
The basic configuration format is as follows:
```yaml
  StorageType: Azure
  Azure:
    Container: kurrentdb-cluster-123-archive
    Authentication: <authentication method> # Default / ConnectionString / SystemAssignedIdentity / UserAssignedIdentity
```

The following `Authentication` methods are supported:
- `Default`  
  This method uses the [DefaultAzureCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet) class and loads credentials from a list of predefined locations.
  A service URL must be provided using the `ConnectionStringOrServiceUrl` configuration option. This authentication method is not recommended for production use by Microsoft. You can use it with the _Azure CLI_ (among other methods) to quickly test if your setup works.  
  _Example:_  
```yaml
  StorageType: Azure
  Azure:
    Container: kurrentdb-cluster-123-archive
    Authentication: Default
    ConnectionStringOrServiceUrl: https://your-storage-account.blob.core.windows.net/
```

- `ConnectionString`  
  This method uses a [connection string](https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string) to authenticate with Azure.
  The connection string must be supplied using the `ConnectionStringOrServiceUrl` configuration option. This authentication method is suitable when your KurrentDB cluster runs outside Azure.  
  _Example:_  
```yaml
  StorageType: Azure
  Azure:
    Container: kurrentdb-cluster-123-archive
    Authentication: ConnectionString
    ConnectionStringOrServiceUrl: DefaultEndpointsProtocol=https;AccountName=<your-storage-account>;AccountKey=<your-account-key>;EndpointSuffix=core.windows.net
```

- `SystemAssignedIdentity`  
  This method uses a [system-assigned managed identity](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview#managed-identity-types) to authenticate with Azure.
  A service URL must be provided using the `ConnectionStringOrServiceUrl` configuration option. This authentication method is suitable when your KurrentDB cluster runs in virtual machines inside Azure.  
  _Example:_  
```yaml
  StorageType: Azure
  Azure:
    Container: kurrentdb-cluster-123-archive
    Authentication: SystemAssignedIdentity
    ConnectionStringOrServiceUrl: https://your-storage-account.blob.core.windows.net/
```

- `UserAssignedIdentity`  
  This method uses a [user-assigned managed identity](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview#managed-identity-types) to authenticate with Azure.
  A service URL must be provided using the `ConnectionStringOrServiceUrl` configuration option and the client ID of the user-assigned managed identity must be supplied with the `UserAssignedClientId` configuration option. This authentication method is suitable when your KurrentDB cluster runs in virtual machines inside Azure.  
  _Example:_  
```yaml
  StorageType: Azure
  Azure:
    Container: kurrentdb-cluster-123-archive
    Authentication: UserAssignedIdentity
    UserAssignedClientId: 2d8e2e8c-8b17-4d63-8c20-3b7e8a7cbb6b
    ConnectionStringOrServiceUrl: https://your-storage-account.blob.core.windows.net/
```

## Metrics

The metrics relevant to Archiving in particular are `kurrentdb_logical_chunk_read_distribution_bucket` and `kurrentdb_io_record_read_duration_seconds_bucket` described in the [metrics](../diagnostics/metrics.md#events) documentation.

The panels are available in the `Events Served` section of the [miscellaneous panels](https://grafana.com/grafana/dashboards/22823) dashboard.

![Chunk Read Distribution](images/archiving/chunk-read-distribution.png)

![Reads by Source](images/archiving/reads-by-source.png)

## Limitations

This initial release has several limitations that we intend to improve in future releases.

Work to improve the following limitations is in progress:
- Requests that read the archive can cause other reads to be queued behind them, resulting in higher read latency if the archive is being accessed frequently.

Work to improve the following limitations is planned:
- Once uploaded to the archive, the chunks there are not scavenged any further.
- Clients cannot yet opt out of their read reading from the archive.
- Repeated reads of the same part of the archive are not cached locally.