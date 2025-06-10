---
order: 1
---

# Release notes

This page contains the release notes for EventStoreDB 22.10 and 22.6

## [22.10.5](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.5)

20 February 2024

### Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684)

Fixed a potential password leak in the EventStoreDB Projections Subsystem.

Only database instances that use custom projections are affected by this vulnerability.

User passwords may become accessible to those who have access to the chunk files on disk, and users who have read access to system streams. Only users in the `$admins` group can access system streams by default.

Recommended action
* Upgrade EventStoreDB: Kurrent Cloud customers follow the instructions in the [cloud upgrade guide](https://docs.kurrent.io/cloud/dedicated/ops/#upgrading-kurrentdb-version). Otherwise, follow the instructions in the [standard upgrade guide](./upgrade-guide.md).
* Reset the passwords for current and previous members of `$admins` and `$ops` groups.
* If a password was reused in any other system, reset it in those systems to a unique password to follow best practices.

## [22.10.4](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.4)

11 December 2023

### Important fix: Prevent unreplicated data from being visible before truncation

This addresses an issue where unreplicated data could be exposed before truncation in certain edge cases. It was possible for reads and subscriptions to receive these events which would then be truncated.

### Additional fixes

* Updating Persistent Subscriptions no longer clears the filter
* Filtered subscriptions now checkpoint on the correct intervals

## [22.10.3](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.3)

14 September 2023

### In this release

#### Fix live reloading of certificates

There was a regression in version 22.10.0 that prevented certificates from being reloaded through the `/admin/reloadconfig` endpoint or through a SIGHUP signal. This means that it was not possible to perform a rolling certificate upgrade across a cluster on previous versions of 22.10.

Now that this is fixed you can perform a rolling certificate upgrade by following the [documented steps](./operations.md#certificate-update-upon-expiry)

#### Support bloom filters for very large index files

This patch fixes two issues that could cause the server to exit when trying to create or read very large bloom filters.

The first issue occurred when EventStoreDB tried to create a bloom filter for a PTable larger than 400gb (around 16 billion events). This caused EventStoreDB to create a bloom filter larger than the maximum allowed size, and caused the server to exit. The size of bloom filters is now clamped to the maximum allowed size.

The second issue is Linux-specific. Bloom filters larger than around 2gb could not be opened due to the way they were being read off disk by EventStoreDB. This caused the bloom filters to not be used by the server. This has now been fixed.

## [22.10.2](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.2)

15 May 2023

### In this release

#### "Could not get TimeStamp range for chunk" error during Scavenge

It was possible for scavenging on EventStoreDB 22.10.0 and 22.10.1 to fail with the error message "Could not get TimeStamp range for chunk" if a scavenge had previously been run on an older version of EventStoreDB. This happened when the previous scavenge had scavenged away all of the events in a chunk (leaving it empty) but hadn't scavenged the index.

This case is now correctly handled in 22.10.2

#### Properly report Create/Delete errors for persistent subscriptions

Previously, the gRPC client was not getting notified when the creation or deleting of a persistent subscription failed, resulting in these failed requests just timing out.

This version will now correctly report the fact that the request failed back to the client.

## [22.10.1](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.1)

13 February 2023

### In this release

#### Database checkpoints no longer become inconsistent when running out of disk space

Fixed an issue with checkpoints becoming inconsistent when the database ran out of disk space, causing the EventStoreDB to fail to start with the following error:

```
Prefix/suffix length inconsistency: prefix length(196) != suffix length (0).\nActual pre-position: 45670073. Something is seriously wrong in chunk #199-199 (chunk-000199.000000).
```

Databases already experiencing the above error can be recovered by copying the `chaser.chk` over the `truncate.chk` like you would when restoring a backup. This will truncate the database back to the most recent valid data.

#### Slow persistent subscription consumers no longer slow down other subscribers

Ensure persistent subscriptions messages don't continue on the main queue. This prevents continuations which could take a long time to process from happening on the main queue.

#### Additional fixes

* Checkpoints in filtered $all subscriptions now continue to update after the subscription is live
* Cancel reads already in the reader queues when the gRPC call is cancelled
* Added more information to `SLOW QUEUE MSG` log entries for reads and writes

## [22.10.0](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.10.0)

14 November 2022

### In this release

#### Improved scavenge process

22.10.0 includes the new scavenge process which runs approximately 4-40x faster depending on the circumstances, and uses much less memory than scavenges in the previous versions.

The new scavenge has a few differences to the previous version:

##### Nodes can scavenge to the same point

The new scavenge process saves a scavenge point in the log containing the location that the scavenge will run up to and the timestamp used for checking `maxAge` during the scavenge.

This scavenge point is shared across all of the nodes in the cluster. Any time a scavenge is started on a node, it will first check if there are any existing scavenge points from another node. If there is, it will scavenge up to the latest scavenge point. Otherwise it will create a new one.

In this way, each node in the cluster will have the same data after a scavenge has been run on all of the nodes.

##### Data is removed from the current active chunk

Previously, the most recently written chunk in the database would not be scavenged because the chunk was not marked as complete.

The new scavenge process completes the current chunk when it writes a scavenge point. This means that all of your data is eligible to be scavenged and you no longer have to wait until enough data has been written to complete a chunk to scavenge recently-written data.

##### New scavenge directory

Scavenge accumulates data that it references throughout the current scavenge and subsequent scavenges on the same node. This data is stored in the 'scavenge' directory inside the index directory.

We recommend the scavenge directory is included in backups to save time having to rebuild it next time a scavenge is run after a restore. As before, ensure that scavenge is not running while a backup is taken (this is not necessary when backing up with disk snapshots).

#### Dev certificates

We have introduced a new dev mode to make running a local dev instance of EventStoreDB easier. You can now run a secure single EventStoreDB node on localhost with the command:

```
./EventStore.ClusterNode.exe -–dev
```

This does a few things. It generates a self-signed dev certificate for the node if one isn’t already present, enables AtomPub over HTTP, enables system projections, and starts the secure EventStoreDB instance.

You can then browse to the admin UI or connect a gRPC client with very little hassle.

The benefit to this over insecure mode is that there are no extra configuration changes needed for your clients, and user management is enabled in secure mode.

If you wish to remove the dev certificates created by EventStoreDB, you can run the command:

```
./EventStore.ClusterNode.exe --remove-dev-certs
```

#### Dynamic stream info cache

The Stream Info Cache is the lookup that contains key information about any stream that has recently been read or written to. Having entries in this cache significantly improves write and read performance to cached streams on larger databases.

Previously, this cache size was set to a static value based on the amount of free memory at the time of EventStoreDB starting up. This could result in an unnecessarily large Stream Info Cache, or in too much memory pressure on the node.

As of EventStoreDB 22.10.0, the Stream Info Cache is dynamic by default. This means that it gets resized periodically or under certain conditions based on how much free memory is available.

The dynamic cache deals with the following scenarios more efficiently than the statically-sized one:

* Growth of the database with time
* Heavy operations such as index merges
* Growth in the number of clients connected
* Spike in memory usage of other processes on the system

The StreamInfoCache can still be made static by specifying `--stream-info-cache-capacity`

New statistics for caches are also available under `es.stats.cache.*` in the stats log file.

#### Better support for certificates when using DNS discovery

EventStoreDB 21.10.5 added support for wildcard certificates when using DNS discovery in a cluster.

This means that you don't have to include the IP address in the node's certificate when using `DiscoverViaDns: true.

#### Faster startup times for large databases

When EventStoreDB upgraded to NET5.0, a performance regression in `Directory.EnumerateFiles` made a performance issue in the EventStoreDB code more obvious, which greatly affected startup and truncation times on large databases.

We have reduced the computational complexity of the methods run at startup to lower the impact of the regression and improve startup times.

This fix was introduced in EventStoreDB 21.10.2.

### Breaking changes

#### Proto2 upgraded to Proto3 (TCP)

The server now uses Proto3 for messages sent to the legacy TCP clients. This may impact community clients, especially when it comes to default or required fields as these are no longer supported in Proto3.

#### EmitEnabled and TrackEmittedStreams for projections (gRPC)

Since 22.6.0, the server supports clients setting EmitEnabled and TrackEmittedStreams when creating a projection.

Essentially, the names of `EmitEnabled` and `TrackEmittedStreams` have been swapped in the proto because what the server used to refer to as `TrackEmittedStreams` is, in fact, the correct behaviour for `EmitEnabled`.

Therefore clients that were relying on the old behaviour and just sending `TrackEmittedStreams=true` to enable emitting when creating a projection will continue to work after upgrading the server.

However, the server will now throw when sent a create request with `TrackEmittedStreams=true`, but `EmitEnabled=false`. This is because a code change will be needed when a client library is upgraded, so throwing an error will make that code change more obvious.

## [22.6.0](https://github.com/kurrent-io/KurrentDB/releases/tag/oss-v22.6.0)

10 August 2022

### In this release

#### $all position is available when reading streams over gRPC

The $all position has been added to the result of stream reads, subscriptions, and persistent subscriptions. This means that you can now read an event in a stream and find its corresponding position in the $all stream.

In order to get this functionality, you may need to upgrade your gRPC client to the latest version.

The one caveat is that this is not supported on events written through transactions in the legacy TCP client. For these events, the positions in the events will be the same as before.

#### Better support for the Windows cert store

EventStoreDB 22.6.0 introduces the ability to load the Trusted Root Certificate from the Windows certificate store rather than from a folder.

To use this, provide `TrustedRootCertificateStoreName` and `TrustedRootCertificateStoreLocation` in the settings when configuring your EventStoreDB, and then provide either the `TrustedRootCertificateThumbprint` or `TrustedRootCertificateSubjectName` to specify which certificate to load.

For example, using thumbprint:

```
TrustedRootCertificateStoreLocation: LocalMachine
TrustedRootCertificateStoreName: Root
TrustedRootCertificateThumbprint: {thumbprint}
CertificateStoreLocation: LocalMachine
CertificateStoreName: My
CertificateThumbprint: {thumbprint}
```

Or using SubjectName (this will match any certificate that contains the subject name) :

```
TrustedRootCertificateStoreLocation: LocalMachine
TrustedRootCertificateStoreName: Root
TrustedRootCertificateSubjectName: EventStoreDB CA
CertificateStoreLocation: LocalMachine
CertificateStoreName: My
CertificateSubjectName: eventstoredb-node
```

### Breaking changes for community gRPC clients

EmitEnabled and TrackEmittedStreams for projections

The server now supports clients setting EmitEnabled and TrackEmittedStreams when creating a projection.

Updates for our supported clients will be made after this release, but if you maintain your own client, then you will need to take note of the changes made here:

https://github.com/EventStore/EventStore/pull/3384
https://github.com/EventStore/EventStore/pull/3412

Essentially, the names of `EmitEnabled` and `TrackEmittedStreams` have been swapped in the proto because what the server used to refer to as `TrackEmittedStreams` is, in fact, the correct behaviour for `EmitEnabled`.

Therefore clients that were relying on the old behaviour and just sending `TrackEmittedStreams=true` to enable emitting when creating a projection will continue to work after upgrading the server.

However, the server will now throw when sent a create request with `TrackEmittedStreams=true`, but `EmitEnabled=false`. This is because a code change will be needed when a client library is upgraded, so throwing an error will make that code change more obvious.
