# Secondary indexes

KurrentDB v25.1 introduces support for secondary indexes, allowing for efficient querying of event streams based on indexed fields.

The initial version supports two default secondary indexes:
- Category index: Indexes events by their category, enabling quick retrieval of all events within a specific category.
- Event type index: Indexes events by their event type, allowing for efficient querying of events of a particular type.

## Introduction

The primary aim for these default secondary indexes is to enhance read performance for common query patterns as well as to remove the need for running system projections that create linked streams for categories (`$by-category`) and event types (`$by-event-type`).

::: important How system projections work
Category and event type system projections create streams of link events that index events by category or event type. When reading streams of links, KurrentDB must resolve each link event to the original event, which adds overhead to read operations. Also, when streams are being truncated or deleted, link events remain in the database because KurrentDB cannot remove events from streams other than the one being truncated or deleted. This can lead to increased storage usage over time. Statistics we collected from production systems indicate that in systems that keep the database size contained by deleting unused data, up to 50% of the database size can be due to link events created by these system projections, where old chunk files primarily consist of link events that are pointing to deleted events and thus cannot be resolved, taking up to 90% of disk space in those chunk files. As you can imagine, replaying events from the beginning of time in such systems can be very inefficient because first the link events need to be read and then resolved to the original events, many of which may no longer exist.
:::

Differences between secondary indexes and system projections:
- Secondary indexes are stored outside the main event log, allowing for efficient querying without the overhead of link resolution. When an event is appended to a stream, KurrentDB automatically updates the relevant secondary indexes.
- Entries in secondary indexes can be removed when the original event is deleted, ensuring that indexes only contain records that can be resolved. **This is not yet implemented in v25.1 but is planned for a future release.**
- Entries in secondary indexes are more compact than link events, reducing storage overhead.
- Link events are stored in the database log files, but are also indexed in the default index. Secondary indexes are not using the database log files for storage and have no effect on the default index size.

::: note Example
On a database with 130 million events (~400 bytes each) distributed across 1 million streams, using category and event type system projections resulted in 280 million link events. The database files without the link events were around 48 GB, while the database files with link events were around 102 GB. The default index size without link events was around 3.2 GB, while with link events it was around 8.7 GB. The total impact of storing link events on storage size for that particular dataset was around 60 GB, which is roughly 100%. In contrast, the secondary indexes for category and event type were only around 2.2 GB in total.

Note that this example also counts links produced by `$streams` and `$stream-by-category` system projections, but those projections produce only one link per stream, so their impact on the database size is marginal compared to category and event type projections.
:::

## Enabling secondary indexes

Secondary indexes feature is _enabled by default_. After KurrentDB is upgraded to v25.1, it will start building the secondary indexes in the background. Depending on the database size, this process can take from minutes to hours. During this time, you can still use the database normally, but queries that would use the secondary indexes may not return complete results until the indexes are fully built. The database would also report high increase in reads count until the initial indexing is complete.

::: warning
Extensive reads during the initial indexing process may impact the performance of other operations on the database. It is recommended to perform the upgrade during a maintenance window or a period of low activity.
:::

To disable secondary indexes, set the following configuration options in the `kurrentdb.conf` file:

```yaml
SecondaryIndexing:
  Enabled: false
```

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

## Using secondary indexes

Once the secondary indexes are built, you can use them to efficiently query events by category or event type. The KurrentDB client libraries provide methods to query events using secondary indexes.

It is possible to use secondary indexes for both read and subscribe operations. KurrentDB supports that by using a special kind of filter that is available for all operations that read from or subscribe to `$all`. The main difference between streams of links like category, and indexes, is that indexes act as a filter and streams of links are actual streams. Therefore, index reads do not return link events, do not use `resolveLinkTos` setting, and do not provide a sequential event numbers for returned events. Read and subscribe operations therefore use the log position of the original event for tracking progress instead of the event number in the index.

For example, to read all events of type `OrderPlaced` using the event type secondary index, you would use the following code with the event type system projection:

```csharp
var readFromEtStream = client.ReadStreamAsync(
    Direction.Forwards,
    "$et-OrderPlaced",
    StreamPosition.Start,
    resolveLinkTos: true,
    maxCount: 1000
);
```

With the event type secondary index, you would use the following code:

```csharp
var read = client.ReadAllAsync(
    Direction.Forwards,
    Position.Start,
    StreamFilter.Prefix("$idx-et-OrderPlaced"),
    maxCount: 1000
);
```

Any client API that supports read and subscribe operation to `$all` with filters can use secondary indexes in the same way. There are a few things to consider:
- Use stream prefix filter
- Instead of a collection of stream prefixes, provide _one_ index name
- Index names are prefixed with `$idx-`, e.g. `$idx-ce-CATEGORYNAME` or `$idx-et-EVENTTYPE`

## Considerations

When using secondary indexes, keep in mind the following:
- Secondary indexes are eventually consistent. There may be a slight delay between when an event is written and when it appears in the secondary index.
- Secondary indexes are built in the background and may take some time to complete the initial indexing, especially for large databases.
- Secondary indexes currently support only category and event type indexing. Future releases may introduce additional index types.
- Read and subscribe operations on secondary indexes need to use the log position for tracking progress instead of event numbers, as indexes do not provide sequential event numbers.

In terms of storage and performance, using secondary indexes can lead to:
- Reduced storage usage compared to link events created by system projections by up to 50%.
- Improved read performance for queries that filter by category or event type, as the need for link resolution is eliminated. Our tests indicate up to 10 times faster reads for such queries, but only with larger page sizes. We recommend reading at least 1000 records for regular read operations. Subscriptions to secondary indexes use 2048 as the default page size compared with 32 for regular subscriptions, which helps improve performance for subscription use cases.

## Limitations

The first version of secondary indexes has some limitations that will be addressed in future releases:
- Deletion of events does not remove entries from secondary indexes. This may lead to index entries pointing to non-existent events. It might impact speed of read operations but won't produce any errors as index records that cannot be resolved would be skipped on the server side. Future releases will implement automatic cleanup of index entries when events are deleted.
- The previous point also indicates that reading from secondary indexes would not grant retention policies such as `MaxAge` or `MaxCount`. Events that have been deleted may still be returned by index reads until the index entries are cleaned up in a future release.
- Because read and subscribe operations using secondary indexes currently rely on client API for reading `$all`, those operations can only be performed by users that are part of the `$admins` group. Future releases will enable reading from `$all` and secondary indexes without requiring admin privileges.

## Backup and restore

KurrentDB uses an embedded DuckDB for storing index data. The DuckDB database files are stored in the same directory as the main KurrentDB database files. Unlike KurrentDB database files, DuckDB files are not append-only and can be modified in place.

Because of that, using file-system based backup solutions (e.g., file copies) may lead to inconsistent backups of secondary indexes if the DuckDB files are modified during the backup process. Therefore, the recommended way to perform backup and restore for KurrentDB v25.1 and later is to use volume snapshots instead of file copies. Volume snapshots ensure that all files, including DuckDB files, are captured in a consistent state.