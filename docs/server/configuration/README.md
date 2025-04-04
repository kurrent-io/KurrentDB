---
dir:
  text: Configuration
  order: 2
---

# Configuring the server

KurrentDB has a number of configuration options that are described in detail in the sections to the left.

The options have sensible defaults, but those might not suit your specific needs and can be customised.

There are multiple ways to configure KurrentDB. They are described below in order of increasing precedence:

### Version and help

Use the `--version` parameter in the command line to check the installed version of KurrentDB. For example:

::: tabs#os
@tab Linux
```bash:no-line-numbers
$ kurrentd --version
KurrentDB version 25.0.0.1673-build.1 ee (0de5e4be-c415-4230-95d4-0f0d3b1d4a18/80248a9214b3412d5051bac58168ee5666e6cb97)
```

@tab Windows
```powershell
> KurrentDB.exe --version
KurrentDB version 25.0.0.1673-build.1 ee (0de5e4be-c415-4230-95d4-0f0d3b1d4a18/80248a9214b3412d5051bac58168ee5666e6cb97)
```
:::

The full list of available options is available from the currently installed server by using the `--help`
option in the command line.

## Configuration files

### YAML

The default configuration file name is `kurrentdb.conf`, and it is located in:
- **Linux:** `/etc/kurrentdb/`
- **Windows:** KurrentDB installation directory

This configuration file has a YAML format. Options can be set as follows:

```yaml
---
Db: "/volumes/data"
Log: "/kurrentdb/logs"
ReaderThreadsCount: 4
```

Nested configuration options are expressed in the natural way:

```yaml
UserCertificates:
  Enabled: true
```

::: tip 
You need to use the three dashes at the top of the YAML file, and spaces for indentation.
:::

The path to the configuration file can be changed with the `KURRENTDB_CONFIG` environment variable, or on the command line with `--config=path-to-file`.

### JSON

KurrentDB looks for JSON configuration files in the `<installation-directory>/config/` directory, and on Linux and OS X, the server additionally looks in `/etc/kurrentdb/config/`. JSON configuration may be split across multiple files or combined into a single file. Apart from the `json` extension, the names of the files is not important.

All configuration options are nested under the `KurrentDB` key.

```json
{
  "KurrentDB": {
    "Db": "/volumes/data",
    "Log": "/kurrentdb/logs",
    "ReaderThreadsCount": 4,

    "UserCertificates": {
      "Enabled": true
    }
  }
}
```

An option set in the JSON configuration overrides the same option set in the YAML configuration.

## Environment variables

When setting options via environment variables, prefix the option with `KURRENTDB_` and use double underscores `__` to indicate nesting.

For example:

```
KURRENTDB_DB
KURRENTDB_LOG
KURRENTDB_READER_THREADS_COUNT
KURRENTDB_USER_CERTIFICATES__ENABLED
```

An option set via an environment variables overrides the same option set in the configuration files.

## Command line

For example, starting KurrentDB with the `--log` option will override the default log files location:

::: tabs#os
@tab Linux
```bash:no-line-numbers
kurrentd --log /tmp/kurrentdb/logs
```

@tab Windows
```bash:no-line-numbers
KurrentDB.exe --log C:\Temp\KurrentDB\Logs
```
:::

Use double underscores `__` to indicate nesting, as with environment variables.

Additional examples:

```
--db
--log
--reader-threads-count
--user-certificates__enabled
```

Command line options override the configuration files and environment variables.

## Testing the configuration

If more than one method is used to configure the server, it may be difficult to discern the effective
configuration when the server starts. You can use the `--what-if` option to better understand the configuration
details.

When you run KurrentDB with the `--what-if` option, it will print out the effective configuration applied from all
available sources (default and custom configuration file, environment variables, and command line parameters)
to the console.

::: details Click here to see a WhatIf example

```
$ KurrentDB.exe --what-if
[13816, 1,12:02:34.627,INF] KurrentDB                      DB VERSION:               25.0.0.1673-build.1 ee (0de5e4be-c415-4230-95d4-0f0d3b1d4a18/80248a9214b3412d5051bac58168ee5666e6cb97, 2025-02-03 15:57:19.547253)
[13816, 1,12:02:34.630,INF] KurrentDB                      OS ARCHITECTURE:          X64
[13816, 1,12:02:34.631,INF] KurrentDB                      OS:                       Windows (Microsoft Windows NT 10.0.22631.0)
[13816, 1,12:02:34.631,INF] KurrentDB                      RUNTIME:                  .NET 8.0.12/89ef51c5d (64-bit)
[13816, 1,12:02:34.631,INF] KurrentDB                      GC:                       3 GENERATIONS IsServerGC: False Latency Mode: Interactive
[13816, 1,12:02:34.632,INF] KurrentDB                      LOGS:                     C:\Users\hayle\Downloads\kurrentdb.25.0.0-build.1\logs
[13816, 1,12:02:34.635,INF] KurrentDB
MODIFIED OPTIONS:
    APPLICATION OPTIONS:
         WHAT IF:                                            true (Command Line)


DEFAULT OPTIONS:
    APPLICATION OPTIONS:
         ALLOW ANONYMOUS ENDPOINT ACCESS:                    False (<DEFAULT>)
         ALLOW ANONYMOUS STREAM ACCESS:                      False (<DEFAULT>)
         ALLOW UNKNOWN OPTIONS:                              False (<DEFAULT>)
         CONFIG:                                             C:\kurrentdb.25.0.0-build.1 (<DEFAULT>)
         DISABLE HTTP CACHING:                               False (<DEFAULT>)
         ENABLE HISTOGRAMS:                                  False (<DEFAULT>)
         HELP:                                               False (<DEFAULT>)
         INSECURE:                                           False (<DEFAULT>)
         LOG FAILED AUTHENTICATION ATTEMPTS:                 False (<DEFAULT>)
         LOG HTTP REQUESTS:                                  False (<DEFAULT>)
         MAX APPEND SIZE:                                    1048576 (<DEFAULT>)
         OVERRIDE ANONYMOUS ENDPOINT ACCESS FOR GOSSIP:      True (<DEFAULT>)
         SKIP INDEX SCAN ON READS:                           False (<DEFAULT>)
         STATS PERIOD SEC:                                   30 (<DEFAULT>)
         TELEMETRY OPTOUT:                                   False (<DEFAULT>)
         VERSION:                                            False (<DEFAULT>)
         WORKER THREADS:                                     0 (<DEFAULT>)

    AUTHENTICATION/AUTHORIZATION OPTIONS:
         AUTHENTICATION CONFIG:                               (<DEFAULT>)
         AUTHENTICATION TYPE:                                internal (<DEFAULT>)
         AUTHORIZATION CONFIG:                                (<DEFAULT>)
         AUTHORIZATION TYPE:                                 internal (<DEFAULT>)
         DISABLE FIRST LEVEL HTTP AUTHORIZATION:             False (<DEFAULT>)

    CERTIFICATE OPTIONS:
         CERTIFICATE RESERVED NODE COMMON NAME:               (<DEFAULT>)
         TRUSTED ROOT CERTIFICATES PATH:                      (<DEFAULT>)

    CERTIFICATE OPTIONS (FROM FILE):
         CERTIFICATE FILE:                                    (<DEFAULT>)
         CERTIFICATE PASSWORD:                               ******** (<DEFAULT>)
         CERTIFICATE PRIVATE KEY FILE:                        (<DEFAULT>)
         CERTIFICATE PRIVATE KEY PASSWORD:                   ******** (<DEFAULT>)

    CERTIFICATE OPTIONS (FROM STORE):
         CERTIFICATE STORE LOCATION:                          (<DEFAULT>)
         CERTIFICATE STORE NAME:                              (<DEFAULT>)
         CERTIFICATE SUBJECT NAME:                            (<DEFAULT>)
         CERTIFICATE THUMBPRINT:                              (<DEFAULT>)
         TRUSTED ROOT CERTIFICATE STORE LOCATION:             (<DEFAULT>)
         TRUSTED ROOT CERTIFICATE STORE NAME:                 (<DEFAULT>)
         TRUSTED ROOT CERTIFICATE SUBJECT NAME:               (<DEFAULT>)
         TRUSTED ROOT CERTIFICATE THUMBPRINT:                 (<DEFAULT>)

    CLUSTER OPTIONS:
         ARCHIVER:                                           False (<DEFAULT>)
         CLUSTER DNS:                                        fake.dns (<DEFAULT>)
         CLUSTER GOSSIP PORT:                                2113 (<DEFAULT>)
         CLUSTER SIZE:                                       1 (<DEFAULT>)
         DEAD MEMBER REMOVAL PERIOD SEC:                     1800 (<DEFAULT>)
         DISCOVER VIA DNS:                                   True (<DEFAULT>)
         GOSSIP ALLOWED DIFFERENCE MS:                       60000 (<DEFAULT>)
         GOSSIP INTERVAL MS:                                 2000 (<DEFAULT>)
         GOSSIP SEED:                                         (<DEFAULT>)
         GOSSIP TIMEOUT MS:                                  2500 (<DEFAULT>)
         LEADER ELECTION TIMEOUT MS:                         1000 (<DEFAULT>)
         NODE PRIORITY:                                      0 (<DEFAULT>)
         QUORUM SIZE:                                        1 (<DEFAULT>)
         READ ONLY REPLICA:                                  False (<DEFAULT>)
         STREAM INFO CACHE CAPACITY:                         0 (<DEFAULT>)
         UNSAFE ALLOW SURPLUS NODES:                         False (<DEFAULT>)

    DATABASE OPTIONS:
         CACHED CHUNKS:                                      -1 (<DEFAULT>)
         CHUNK INITIAL READER COUNT:                         5 (<DEFAULT>)
         CHUNK SIZE:                                         268435456 (<DEFAULT>)
         CHUNKS CACHE SIZE:                                  536871424 (<DEFAULT>)
         COMMIT TIMEOUT MS:                                  2000 (<DEFAULT>)
         DB:                                                 C:\kurrentdb.25.0.0-build.1\data (<DEFAULT>)
         DB LOG FORMAT:                                      V2 (<DEFAULT>)
         DISABLE SCAVENGE MERGING:                           False (<DEFAULT>)
         HASH COLLISION READ LIMIT:                          100 (<DEFAULT>)
         INDEX:                                               (<DEFAULT>)
         INDEX CACHE DEPTH:                                  16 (<DEFAULT>)
         INDEX CACHE SIZE:                                   0 (<DEFAULT>)
         INITIALIZATION THREADS:                             1 (<DEFAULT>)
         MAX AUTO MERGE INDEX LEVEL:                         2147483647 (<DEFAULT>)
         MAX MEM TABLE SIZE:                                 1000000 (<DEFAULT>)
         MAX TRUNCATION:                                     268435456 (<DEFAULT>)
         MEM DB:                                             False (<DEFAULT>)
         MIN FLUSH DELAY MS:                                 2 (<DEFAULT>)
         PREPARE TIMEOUT MS:                                 2000 (<DEFAULT>)
         READER THREADS COUNT:                               0 (<DEFAULT>)
         REDUCE FILE CACHE PRESSURE:                         False (<DEFAULT>)
         SCAVENGE BACKEND CACHE SIZE:                        67108864 (<DEFAULT>)
         SCAVENGE BACKEND PAGE SIZE:                         16384 (<DEFAULT>)
         SCAVENGE HASH USERS CACHE CAPACITY:                 100000 (<DEFAULT>)
         SCAVENGE HISTORY MAX AGE:                           30 (<DEFAULT>)
         SKIP DB VERIFY:                                     False (<DEFAULT>)
         SKIP INDEX VERIFY:                                  False (<DEFAULT>)
         STATS STORAGE:                                      File (<DEFAULT>)
         STREAM EXISTENCE FILTER SIZE:                       256000000 (<DEFAULT>)
         TRANSFORM:                                          identity (<DEFAULT>)
         UNBUFFERED:                                         False (<DEFAULT>)
         UNSAFE DISABLE FLUSH TO DISK:                       False (<DEFAULT>)
         UNSAFE IGNORE HARD DELETE:                          False (<DEFAULT>)
         USE INDEX BLOOM FILTERS:                            True (<DEFAULT>)
         WRITE STATS TO DB:                                  False (<DEFAULT>)
         WRITE THROUGH:                                      False (<DEFAULT>)
         WRITE TIMEOUT MS:                                   2000 (<DEFAULT>)

    DEFAULT USER OPTIONS:
         DEFAULT ADMIN PASSWORD:                             ******** (<DEFAULT>)
         DEFAULT OPS PASSWORD:                               ******** (<DEFAULT>)

    DEV MODE OPTIONS:
         DEV:                                                False (<DEFAULT>)
         REMOVE DEV CERTS:                                   False (<DEFAULT>)

    GRPC OPTIONS:
         KEEP ALIVE INTERVAL:                                10000 (<DEFAULT>)
         KEEP ALIVE TIMEOUT:                                 10000 (<DEFAULT>)

    INTERFACE OPTIONS:
         ADVERTISE HOST TO CLIENT AS:                         (<DEFAULT>)
         ADVERTISE NODE PORT TO CLIENT AS:                   0 (<DEFAULT>)
         CONNECTION PENDING SEND BYTES THRESHOLD:            10485760 (<DEFAULT>)
         CONNECTION QUEUE SIZE THRESHOLD:                    50000 (<DEFAULT>)
         DISABLE ADMIN UI:                                   False (<DEFAULT>)
         DISABLE GOSSIP ON HTTP:                             False (<DEFAULT>)
         DISABLE STATS ON HTTP:                              False (<DEFAULT>)
         ENABLE ATOM PUB OVER HTTP:                          False (<DEFAULT>)
         ENABLE TRUSTED AUTH:                                False (<DEFAULT>)
         ENABLE UNIX SOCKET:                                 False (<DEFAULT>)
         NODE HOST ADVERTISE AS:                              (<DEFAULT>)
         NODE IP:                                            127.0.0.1 (<DEFAULT>)
         NODE PORT:                                          2113 (<DEFAULT>)
         NODE PORT ADVERTISE AS:                             0 (<DEFAULT>)
         REPLICATION HEARTBEAT INTERVAL:                     700 (<DEFAULT>)
         REPLICATION HEARTBEAT TIMEOUT:                      700 (<DEFAULT>)
         REPLICATION HOST ADVERTISE AS:                       (<DEFAULT>)
         REPLICATION IP:                                     127.0.0.1 (<DEFAULT>)
         REPLICATION PORT:                                   1112 (<DEFAULT>)
         REPLICATION TCP PORT ADVERTISE AS:                  0 (<DEFAULT>)

    LOGGING OPTIONS:
         DISABLE LOG FILE:                                   False (<DEFAULT>)
         LOG:                                                C:\kurrentdb.25.0.0-build.1\logs (<DEFAULT>)
         LOG CONFIG:                                         logconfig.json (<DEFAULT>)
         LOG CONSOLE FORMAT:                                 Plain (<DEFAULT>)
         LOG FILE INTERVAL:                                  Day (<DEFAULT>)
         LOG FILE RETENTION COUNT:                           31 (<DEFAULT>)
         LOG FILE SIZE:                                      1073741824 (<DEFAULT>)
         LOG LEVEL:                                          Default (<DEFAULT>)

    PROJECTION OPTIONS:
         FAULT OUT OF ORDER PROJECTIONS:                     False (<DEFAULT>)
         PROJECTION COMPILATION TIMEOUT:                     500 (<DEFAULT>)
         PROJECTION EXECUTION TIMEOUT:                       250 (<DEFAULT>)
         PROJECTION THREADS:                                 3 (<DEFAULT>)
         PROJECTIONS QUERY EXPIRY:                           5 (<DEFAULT>)
         RUN PROJECTIONS:                                    None (<DEFAULT>)
         START STANDARD PROJECTIONS:                         False (<DEFAULT>)
```
:::

::: note 
The server will _not start_ when an unknown configuration option is passed in the configuration file, environment variable, or command line.

As an example, the following will prevent the server from starting:

* `--UnknownConfig` on the command line
* `KURRENTDB_UnknownConfig` through environment variable
* `UnknownConfig: value` in the config file

And will output on `stdout`
only: _Error while parsing options: The option UnknownConfig is not a known option. (Parameter 'UnknownConfig')_.
:::

## Autoconfigured options

Some options are configured at startup to make better use of the available resources on larger instances or
machines.

These options are `StreamInfoCacheCapacity`, `ReaderThreadsCount`, and `WorkerThreads`.

Autoconfiguration does not apply in containerized environments.

### StreamInfoCacheCapacity

This option sets the maximum number of entries in the stream info cache. This lookup contains information on
any stream that has recently been read or written. Having entries in this cache significantly improves the write 
and read performance to cached streams on larger databases.

By default, the cache dynamically resizes based on the amount of free memory. The minimum it can be set to is 100,000 entries.

| Format               | Syntax                                  |
|:---------------------|:----------------------------------------|
| Command line         | `--stream-info-cache-capacity`          |
| YAML                 | `StreamInfoCacheCapacity`               |
| Environment variable | `KURRENTDB_STREAM_INFO_CACHE_CAPACITY`  |

The option is set to 0 by default, which enables dynamic resizing. The default on previous versions of
KurrentDB was 100,000 entries.

::: note
The default value of 0 for `StreamInfoCacheCapacity` might not always be the best value for optimal performance. Ideally, it should be set to double the number of streams in the anticipated working set.

Check the event count in the `$streams` system stream to obtain the total number of streams. This stream is created by the [$streams system projection](../features/projections/system.md#streams-projection).

It should be noted that the total number of streams does not necessarily provide you with the anticipated working set. The working set of streams is the one you intend to read, write, and/or subscribe to actively. This can be much lower than the total number of streams in some instances, especially in systems with many short-lived streams.
:::

### ReaderThreadsCount

This option configures the number of reader threads available to KurrentDB. Increased reader threads
enables more concurrent reads to be processed.

::: warning 
Increasing the reader threads count too high can cause read timeouts if your disk cannot handle the
increased load.
:::

The reader threads count will be set at startup to twice the number of available processors, with a minimum of
four and a maximum of sixteen threads.

| Format               | Syntax                            |
|:---------------------|:----------------------------------|
| Command line         | `--reader-threads-count`          |
| YAML                 | `ReaderThreadsCount`              |
| Environment variable | `KURRENTDB_READER_THREADS_COUNT`  |

The option is set to 0 by default, which enables autoconfiguration. The default on previous versions of
KurrentDB was four threads.


### WorkerThreads

The `WorkerThreads` option configures the number of threads available to the pool of worker services.

At startup, the number of worker threads will be set to ten if there are more than four reader threads. Otherwise,
it will be set to have five threads available.

| Format               | Syntax                      |
|:---------------------|:----------------------------|
| Command line         | `--worker-threads`          |
| YAML                 | `WorkerThreads`             |
| Environment variable | `KURRENTDB_WORKER_THREADS`  |

The option is set to 0 by default, which enables autoconfiguration. The default on previous versions of
KurrentDB was five threads.

