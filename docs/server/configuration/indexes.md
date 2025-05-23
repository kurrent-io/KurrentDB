---
title: "Indexing"
order: 4
---

EventStoreDB stores indexes separately from the main data files, accessing records by stream name.

## Overview

EventStoreDB creates index entries as it processes commit events. It holds these in memory (called 
_memtables_) until it reaches the `MaxMemTableSize` and then persisted on disk in the _index_ folder along
with `.bloomfilter` files and an index map file. The index files are uniquely named, and the index map file
called _indexmap_. The index map describes the order and the level of the index file as well as containing the
data checkpoint for the last written file, the version of the index map file and a checksum for the index map
file. The logs refer to the index files as a _PTable_.

Indexes are sorted lists based on the hashes of stream names. To speed up seeking the correct location in the
file of an entry for a stream, EventStoreDB keeps midpoints to relate the stream hash to the physical offset
in the file.

As EventStoreDB saves more files, they are automatically merged together whenever there are 2 or more files
at the same level into a single file at the next level. Each index entry is 24 bytes and the index file size
is approximately 24Mb per 1M events. The Bloom filter files are approximately 1% of the size of the rest of
the index.

Level 0 is the level of the _memtable_ that is kept in memory. Generally there is only 1 level 0 table unless
an ongoing merge operation produces multiple level 0 tables.

Assuming the default `MaxMemTableSize` of 1M, the index files by level are:

| Level | Number of entries | Size           |
|-------|-------------------|----------------|
| 1     | 1M                | 24MB           |
| 2     | 2M                | 48MB           |
| 3     | 4M                | 96MB           |
| 4     | 8M                | 192MB          |
| 5     | 16M               | 384MB          |
| 6     | 32M               | 768MB          |
| 7     | 64M               | 1536MB         |
| 8     | 128M              | 3072MB         |
| n     | 2^(n-1) * 1M      | 2^(n-1) * 24Mb |

Each index entry is 24 bytes and the index file size is approximately 24Mb per M events.

## Indexing in depth

For general operation of EventStoreDB the following information is not critical but useful for developers
wanting to make changes in the indexing subsystem and for understanding crash recovery and tuning scenarios.

### Index map files

_Indexmap_ files are text files made up of line delimited values. The line delimiter varies based on operating
system, so while you can consider _indexmap_ files valid when transferred between operating systems, if you
make changes to fix an issue (for example, disk corruption) it is best to make them on the same operating
system as the cluster.

The _indexmap_ structure is as follows:

- `hash` - an md5 hash of the rest of the file
- `version` - the version of the _indexmap_ file
- `checkpoint` - the maximum prepare/commit position of the persisted _ptables_
- `maxAutoMergeLevel` - either the value of `MaxAutoMergeLevel` or `int32.MaxValue` if it was not set. This is
  primarily used to detect increases in `MaxAutoMergeLevel`, which is not supported.
- `ptable`,`level`,`index`- List of all the _ptables_ used by this index map with the level of the _ptable_
  and it's order.

EventStoreDB writes _indexmap_ files to a temporary file and then deletes the original and renames the
temporary file. EventStoreDB attempts this 5 times before failing. Because of the order, this operation can
only fail if there is an issue with the underlying file system or the disk is full. This is a 2 phase process,
and in the unlikely event of a crash during this process, EventStoreDB recovers by rebuilding the indexes
using the same process used if it detects corrupted files during startup.

### Writing and merging of index files

Merging _ptables_, updating the _indexmap_ and persisting _memtable_ operations happen on a background thread.
These operations are performed on a single thread with any additional operations queued and completed later.
EventStoreDB runs these operations on a thread pool thread rather than a dedicated thread. Generally there is
only one operation queued at a time, but if merging to _ptables_ at one level causes 2 tables to be available
at the next level, then the next merge operation is immediately queued. While merge operations are in
progress, if EventStoreDB is appending large numbers of events, it may queue 1 or more _memtables_ for
persistence. The number of pending operations is logged.

For safety _ptables_ EventStoreDB is currently merging are only deleted after the new _ptable_ has persisted
and the _indexmap_  updated. In the event of a crash, EventStoreDB recovers by deleting any files not in the
_indexmap_ and reindexing from the prepare/commit position stored in the _indexmap_ file.

### Manual merging

If you have set the maximum level ([`MaxAutoMergeIndexLevel`](#auto-merge-index-level)) for automatically
merging indexes, then you need to trigger merging indexes above this level manually by using
the `/admin/mergeindexes` endpoint, or the es-cli tool.

Triggering a manual merge causes EventStoreDB to merge all tables that have a level equal to the maximum merge
level or above into a single table. If there is only 1 table at the maximum level or above, no merge is
performed.

### Stream existence filter

The _Stream Existence Filter_ is a pair of files in the index directory.

- `/index/stream-existence/streamExistenceFilter.chk`
- `/index/stream-existence/streamExistenceFilter.dat`

It is made up of a persisted Bloom filter (the `.dat` file), and a checkpoint (the `.chk` file) that records
where in the log the filter has processed up to. As per the documented backup procedure, the checkpoint needs
to be backed up before the dat file.

The Stream Existence Filter is used when reading and writing to quickly determine whether a stream _might
exist_ or _definitely does not exist_. If a stream definitely does not exist then EventStoreDB can skip
looking through the index to find information about it. This is particularly useful when creating new
streams (i.e. appending to stream that did not previously exist).

Additional information can be found in this [`blog post`](https://www.eventstore.com/blog/bloom-filters).

## Configuration options

The configuration options that affect indexing are:

| Option                                                       | What's it for                                                                                                                                         |
|:-------------------------------------------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------|
| [`Index`](#index-location)                                   | Where the indexes are stored                                                                                                                          |
| [`MaxMemTableSize`](#memtable-size)                          | How many entries to have in memory before writing out to disk                                                                                         |
| [`IndexCacheDepth`](#index-cache-depth)                      | Sets the minimum number of midpoints to calculate for an index file                                                                                   |
| [`SkipIndexVerify`](#skip-index-verification)                | Tells the server to not verify indexes on startup                                                                                                     |
| [`MaxAutoMergeIndexLevel`](#auto-merge-index-level)          | The maximum level of index file to merge automatically before manual merge                                                                            |
| [`StreamExistenceFilterSize`](#stream-existence-filter-size) | Size in bytes of the stream existence filter                                                                                                          |
| [`IndexCacheSize`](#index-cache-size)                        | Maximum number of entries in each index LRU cache                                                                                                     |
| [`UseIndexBloomFilters`](#use-index-bloom-filters)           | Feature flag which can be used to disable the index Bloom filters                                                                                     |

Read more below to understand these options better.

### Index location

| Format               | Syntax             |
|:---------------------|:-------------------|
| Command line         | `--index`          |
| YAML                 | `Index`            |
| Environment variable | `EVENTSTORE_INDEX` | 

**Default**: data files location

`Index` affects the location of the index files. We recommend you place index files on a separate drive to
avoid competition for IO between the data, index and log files.

### Memtable size

| Format               | Syntax                          |
|:---------------------|:--------------------------------|
| Command line         | `--max-mem-table-size`          |
| YAML                 | `MaxMemTableSize`               |
| Environment variable | `EVENTSTORE_MAX_MEM_TABLE_SIZE` | 

**Default**: `1000000`

`MaxMemTableSize` affects disk IO when EventStoreDB writes files to disk, index seek time and database startup
time. The default size is a good tradeoff between low disk IO and startup time. Increasing
the `MaxMemTableSize` results in longer database startup time because a node has to read through the data
files from the last position in the `indexmap` file and rebuild the in memory index table before it starts.

<!-- TODO: Polish a little more -->

Increasing `MaxMemTableSize` also decreases the number of times EventStoreDB writes index files to disk and
how often it merges them together, which increases IO operations. It also reduces the number of seek
operations when stream entries span multiple files as EventStoreDB needs to search each file for the stream
entries. This affects streams written to over longer periods of time more than streams written to over a
shorter time, where time is measured by the number of events created, not time passed. This is because streams
written to over longer time periods are more likely to have entries in multiple index files.

### Index cache depth

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--index-cache-depth`          |
| YAML                 | `IndexCacheDepth`              |
| Environment variable | `EVENTSTORE_INDEX_CACHE_DEPTH` | 

**Default**: `16`

`IndexCacheDepth` affects how many midpoints EventStoreDB calculates for an index file which affects file
size slightly, but can affect lookup times significantly. Looking up a stream entry in a file requires a
binary search on the midpoints to find the nearest midpoint, and then a seek through the entries to find the
entry or entries that match. Increasing this value decreases the second part of the operation and increase the
first for extremely large indexes.

**The default value of 16** results in files up to about 1.5 GB in size being fully searchable through
midpoints. After that a maximum distance between midpoints of 4096 bytes for the seek, which is buffered from
disk, up to a maximum level of 2 TB where the seek distance starts to grow. Reducing this value can relieve a
small amount of memory pressure in highly constrained environments. Increasing it causes index files larger
than 1.5 GB, and less than 2 TB to have more dense midpoint populations which means the binary search is not
used for long before switching back to scanning the entries between. The maximum number of entries scanned in
this way is `distance/24b`, so with the default setting and a 2TB index file this is approximately 170
entries. Most clusters should not need to change this setting.

### Skip index verification

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--skip-index-verify`          |
| YAML                 | `SkipIndexVerify`              |
| Environment variable | `EVENTSTORE_SKIP_INDEX_VERIFY` | 

**Default**: `false`

`SkipIndexVerify` skips reading and verification of index file hashes during startup. Instead of recalculating
midpoints when EventStoreDB reads the file, it reads the midpoints directly from the footer of the index file.
You can set `SkipIndexVerify` to `true` to reduce startup time in exchange for the acceptance of a small risk
that the index file becomes corrupted. This corruption could lead to a failure if you read the corrupted
entries, and a message saying the index needs to be rebuilt. You can safely disable this setting for ZFS on
Linux as the filesystem takes care of file checksums.

In the event of corruption indexes will be rebuilt by reading through all the chunk files and recreating the
indexes from scratch.

### Auto-merge index level

| Format               | Syntax                                  |
|:---------------------|:----------------------------------------|
| Command line         | `--max-auto-merge-index-level`          |
| YAML                 | `MaxAutoMergeIndexLevel`                |
| Environment variable | `EVENTSTORE_MAX_AUTO_MERGE_INDEX_LEVEL` | 

**Default**: `2147483647`

`MaxAutoMergeIndexLevel` allows you to specify the maximum index file level to automatically merge. By default
EventStoreDB merges all levels. Depending on the specification of the host running EventStoreDB, at some point
index merges will use a large amount of disk IO.

For example:

> Merging 2 level 7 files results in at least 3072 MB reads (2 \* 1536 MB), and 3072 MB writes while merging 2 level 8 files together results in at least 6144 MB reads (2 \* 3072 MB) and 6144 MB writes. Setting `MaxAutoMergeLevel` to 7 allows all levels up to and including level 7 to be automatically merged, but to merge the level 8 files together, you need to trigger a manual merge. This manual merge allows better control over when these larger merges happen and which nodes they happen on. Due to the replication process, all nodes tend to merge at about the same time.

### Stream existence filter size

| Format               | Syntax                                    |
|:---------------------|:------------------------------------------|
| Command line         | `--stream-existence-filter-size`          |
| YAML                 | `StreamExistenceFilterSize`               |
| Environment variable | `EVENTSTORE_STREAM_EXISTENCE_FILTER_SIZE` | 

**Default**: `256000000`

`StreamExistenceFilterSize` is the amount of memory & disk space, in bytes, to use for the stream existence
filter. This should be set to roughly the maximum number of streams you expect to have in your database, i.e
if you expect to have a max of 500 million streams, use a value of 500000000. The value you select should also
fit entirely in memory to avoid any performance degradation. Use 0 to disable the filter.

Upgrading to a version of EventStoreDB that supports the Stream Existence Filter requires the filter to be
built - unless it is disabled. This will take approximately as long as it takes to read through the whole
index.

Resizing the filter will also cause a full rebuild of the filter.

### Index cache size

| Format               | Syntax                        |
|:---------------------|:------------------------------|
| Command line         | `--index-cache-size`          |
| YAML                 | `IndexCacheSize`              |
| Environment variable | `EVENTSTORE_INDEX_CACHE_SIZE` | 

**Default**: `0`

`IndexCacheSize` is the maximum number of entries in each index LRU cache. The cache size is set to 0 (off) by
default because it has an associated memory overhead and can be detrimental to workloads that produce a lot of
cache misses. The cache is, however, well suited to read-heavy workloads of long-lived streams.

The index LRU cache is only created for index files that have Bloom filters.

### Use index Bloom filters

| Format               | Syntax                               |
|:---------------------|:-------------------------------------|
| Command line         | `--use-index-bloom-filters`          |
| YAML                 | `UseIndexBloomFilters`               |
| Environment variable | `EVENTSTORE_USE_INDEX_BLOOM_FILTERS` | 

**Default**: `true`

`UseIndexBloomFilters` is a feature flag which can be used to disable the index Bloom filters. This should not
be necessary and this flag will be removed in a future release, but is provided for safety since the index
Bloom filters are a new feature. Please contact EventStore if you discover some need to disable this feature.

Unless this flag is set to false, EventStoreDB creates a `.bloomfilter` file for each new PTable. The Bloom
filter describes which streams are present in the PTable. This speeds up stream reads since EventStoreDB can
avoid searching in index files do not contain the stream.

Note that immediately after upgrading to a version of EventStoreDB that produces index Bloom filters, no Bloom
filters will yet exist. Either wait for new PTables to be produced with Bloom filters in the natural course of
writing/merging/scavenging PTables, or rebuild the index for immediate generation.

## Tuning indexes

For most EventStoreDB clusters, the default settings are enough to give consistent and good performance. For
clusters with larger numbers of events, or those that run in constrained environments the configuration
options allow for some tuning to meet operational constraints.

The most common optimization needed is to set a `MaxAutoMergeLevel` to avoid large merges occurring across all
nodes at approximately the same time. Large index merges use a lot of IOPS and in IOPS constrained
environments it is often desirable to have better control over when these happen. Because increasing this
value requires an index rebuild you should start with a higher value and decrease until the desired balance
between triggering manual merges (operational cost) and automatic merges (IOPS) cost. The exact value to set
this varies between environments due to IOPS generated by other operations such as read and write load on the
cluster.

For example:

> A cluster with 3000 256b IOPS can read/write about 0.73Gb/sec (This level of IOPS represents a small cloud instance). Assuming sustained read/write throughput of 0.73Gb/s. When an index merge of level 7 or above starts, it consumes as many IOPS up to all on the node until it completes. Because EventStoreDB has a shared nothing architecture for clustering this operation is likely to cause all nodes to appear to stall simultaneously as they all try and perform an index merge at the same time. By setting `MaxAutoMergeLevel` to 6 or below you can avoid this, and you can run the merge on each node individually keeping read/write latency in the cluster consistent.
