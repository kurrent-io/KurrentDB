---
order: 1
---

# Database settings

On this page, you find settings that tune the database server behaviour. Only modify these settings if you know what you are doing or when requested by Kurrent support personnel.

## Database

Settings described below are related to the database behaviour, like the location of the database files, in-memory database, and verification, and caches.

### Database location

KurrentDB has a single database, which is spread across ever-growing number of physical files on the file system. Those files are called chunks and new data is always appended to the end of the latest chunk. When the chunk grows over 256 MiB, the server closes the chunk and opens a new one.

Normally, you'd want to keep the database files separated from the OS and other application files. The `Db` setting tells KurrentDB where to put those chunk files. If the database server doesn't find anything at the specified location, it will create a new database.

| Format               | Syntax          |
|:---------------------|:----------------|
| Command line         | `--db`          |
| YAML                 | `Db`            |
| Environment variable | `KURRENTDB_DB`  |

**Default**: the default database location is platform specific. On Windows, the database will be stored in the `data` directory inside the KurrentDB installation location. On Linux, it will be `/var/lib/kurrentdb`.

### In-memory database

When running KurrentDB for educational purposes or in an automated test environment, you might want to prevent it from saving any data to the disk. KurrentDB can keep the data in memory as soon as it has enough available RAM. When you shut down the instance that uses in-memory database, all the data will be lost.

| Format               | Syntax              |
|:---------------------|:--------------------|
| Command line         | `--mem-db`          |
| YAML                 | `MemDb`             |
| Environment variable | `KURRENTDB_MEM_DB`  |

**Default**: `false`

### Skip database verification

When the database node restarts, it checks the database files to ensure they aren't corrupted. It is a lengthy process and can take hours on a large database. KurrentDB normally flushes every write to disk, so database files are unlikely to get corrupted. In an environment where nodes restart often for some reason, you might want to disable the database verification to allow faster startup of the node.

| Format               | Syntax                      |
|:---------------------|:----------------------------|
| Command line         | `--skip-db-verify`          |
| YAML                 | `SkipDbVerify`              |
| Environment variable | `KURRENTDB_SKIP_DB_VERIFY`  |

**Default**: `false`

### Chunk cache

You can increase the number of chunk files that are cached if you have free memory on the nodes.

The [stats response](../diagnostics/README.md#statistics) contains two fields: `es-readIndex-cachedRecord` and `es-readIndex-notCachedRecord`. Statistic values for those fields tell you how many times a chunk file was retrieved from the cache and from the disk. We expect the two most recent chunks (the current chunk and the previous one) to be most frequently accessed and therefore cached.

If you observe that the `es-readIndex-notCachedRecord` stats value gets significantly higher than the `es-readIndex-cachedRecord`, you can try adjusting the chunk cache.

The setting `ChunkCacheSize` tells the server how much memory it can use to cache chunks (in bytes). The chunk size is 256 MiB max, so the default cache size (two chunks) is 0.5 GiB. To increase the cache size to four chunks, you can set the value to 1 GiB (`1073741824` bytes).

Remember that only the latest chunks get cached. Also consider that the OS has its own file cache and increasing the chunk cache might not bring the desired performance benefit.

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--chunks-cache-size`          |
| YAML                 | `ChunksCacheSize `             |
| Environment variable | `KURRENTDB_CHUNKS_CACHE_SIZE`  |

**Default**: `536871424`

You would also need to adjust the `CachedChunks` setting to tell the server how many chunk files you want to keep in cache. For example, to double the number of cached chunks, set the `CachedChunks` setting to `4`.

| Format               | Syntax                     |
|:---------------------|:---------------------------|
| Command line         | `--cached-chunks`          |
| YAML                 | `CachedChunks `            |
| Environment variable | `KURRENTDB_CACHED_CHUNKS`  |

**Default**: `-1` (all)

### Prepare and Commit timeouts

Having prepare and commit timeouts of 2000 ms (default) means that any write done on the cluster may take up to 2000 ms before the server replies to the client that this write has timed out.

Depending on your client operation timeout settings (default is 7 seconds), increasing the timeout may block a client for up to the minimum of those two timeouts which may reduce the throughput if set to a large value. The server also needs to keep track of all these writes and retries in memory until the time out is over. If a large number of them accumulate, it may slow things down.

| Format               | Syntax                          |
|:---------------------|:--------------------------------|
| Command line         | `--prepare-timeout-ms`          |
| YAML                 | `PrepareTimeoutMs`              |
| Environment variable | `KURRENTDB_PREPARE_TIMEOUT_MS`  |

**Default**: `2000` (in milliseconds)

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--commit-timeout-ms`          |
| YAML                 | `CommitTimeoutMs`              |
| Environment variable | `KURRENTDB_COMMIT_TIMEOUT_MS`  |

**Default**: `2000` (in milliseconds)

### Disable flush to disk

:::warning
Using this option might cause data loss.
:::

This will prevent KurrentDB from forcing the flush to disk after writes. Please note that this is unsafe in case of a power outage.

With this option enabled, KurrentDB will still write data to the disk at the application level but not necessarily at the OS level. Usually, the OS should flush its buffers at regular intervals or when a process exits but it is something that's opaque to KurrentDB.

| Format               | Syntax                                    |
|:---------------------|:------------------------------------------|
| Command line         | `--unsafe-disable-flush-to-disk`          |
| YAML                 | `UnsafeDisableFlushToDisk`                |
| Environment variable | `KURRENTDB_UNSAFE_DISABLE_FLUSH_TO_DISK`  |

**Default**: `false`

### Minimum flush delay

The minimum flush delay in milliseconds.

<!-- TODO -->

| Format               | Syntax                           |
|:---------------------|:---------------------------------|
| Command line         | `--min-flush-delay-ms`           |
| YAML                 | `MinFlushDelayMs`                |
| Environment variable | `KURRENTDB_MIN_FLUSH_DELAY_MS `  |

**Default**: `2` (ms)

## Threading

Settings described below are related to the threading model used by KurrentDB.

### Worker threads

A variety of undifferentiated work is carried out on general purpose worker threads, including sending over a network, authentication, and completion of HTTP requests.

Increasing the number of threads beyond that necessary to satisfy the workload generated by other components in the system may result in additional unnecessary context switching overhead.

| Format               | Syntax                      |
|:---------------------|:----------------------------|
| Command line         | `--worker-threads`          |
| YAML                 | `WorkerThreads`             |
| Environment variable | `KURRENTDB_WORKER_THREADS`  |

**Default**: `5`

### Reader threads count

Reader threads are used for all read operations on data files - whether the requests originate from the client or internal requests to the database. There are a number of things that cause operations to be dispatched to reader threads, including:

- All index reads, including those to service stream and event number-based reads and for idempotent handling of write operations when an expected version is specified.
- Data to service requests originating from either the HTTP or client APIs.
- Projections needing to read data for processing.
- Authorization needing to read access control lists from stream metadata.

| Format               | Syntax                            |
|:---------------------|:----------------------------------|
| Command line         | `--reader-threads-count`          |
| YAML                 | `ReaderThreadsCount`              |
| Environment variable | `KURRENTDB_READER_THREADS_COUNT`  |

**Default**: `4`

Reads are queued until a reader thread becomes available to service them. If an operation doesn't complete within an internal deadline window, a disk operation is not dispatched by the worker thread which processes the operation.

The size of read operations is dependent on the size of the events appended, not on the database chunk size, which has a fixed logical size. Larger reads (subject to the operating system page size) result in more time being spent in system calls and less availability of reader threads.

A higher reader count can be useful, if disks are able to support more concurrent operations. Context switching incurs additional costs in terms of performance. If disks are already saturated, adding more reader threads can exacerbate that issue and lead to more failed requests.

Increasing the count of reader threads can improve performance up to a point, but it is likely to rapidly tail off once that limit is reached.
