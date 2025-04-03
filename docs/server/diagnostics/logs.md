---
title: "Logs"
order: 1
---

# Database logs

KurrentDB logs its internal operations to the console (stdout) and to log files. The default location of
the log files and the way to change it is described [below](#logs-location).

There are a few options to change the way how KurrentDB produces logs and how detailed the logs should be.

::: warning
KurrentDB logs may contain sensitive information such as stream names, usernames, and projection definitions.
:::

## Log format

KurrentDB uses the structured logging in JSON format that is more machine-friendly and can be ingested by
vendor-specific tools like Logstash or Datadog agent.

Here is how the structured log looks like:

```json
{
	"@t": "2025-02-03T18:11:53.8612833+01:00",
	"@mt": "PROJECTIONS SUBSYSTEM: All components started for Instance: {instanceCorrelationId}",
	"@l": "Information",
	"@i": 669867955,
	"instanceCorrelationId": "9bfbea73-b3de-45d8-8586-caefaa0bf05f",
	"SourceContext": "EventStore.Projections.Core.ProjectionsSubsystem",
	"ProcessId": 19812,
	"ThreadId": 9
} {
	"@t": "2025-02-03T18:11:53.8614272+01:00",
	"@mt": "========== [{httpEndPoint}] Sub System '{subSystemName}' initialized.",
	"@l": "Information",
	"@i": 1147237149,
	"httpEndPoint": "127.0.0.1:2113",
	"subSystemName": "Projections",
	"SourceContext": "EventStore.Core.Services.VNode.ClusterVNodeController",
	"ProcessId": 19812,
	"ThreadId": 16
} {
	"@t": "2025-02-03T18:11:53.8630025+01:00",
	"@mt": "Allowed Connectors: {AllowedConnectors}",
	"@l": "Information",
	"@i": 542710717,
	"AllowedConnectors": ["SerilogSink", "HttpSink"],
	"SourceContext": "EventStore.Connectors.Management.ConnectorsLicenseService",
	"ProcessId": 19812,
	"ThreadId": 22
} {
	"@t": "2025-02-03T18:11:53.8633385+01:00",
	"@mt": "PROJECTIONS: No projections were found in {stream}, starting from empty stream",
	"@l": "Debug",
	"@i": 2065058799,
	"stream": "$projections-$all",
	"SourceContext": "EventStore.Projections.Core.Services.Management.ProjectionManager",
	"ProcessId": 19812,
	"ThreadId": 9
}
```

This format is aligned with the [CLEF format](https://clef-json.org/).

## Logs location

Log files are located in `/var/log/kurrentdb` for Linux and macOS, and in the `logs` subdirectory of the
KurrentDB installation directory on Windows. You can change the log files location using the `Log`
configuration option.

::: tip
Moving logs to a separate storage might improve the database performance if you keep the default
verbose log level.
:::

| Format               | Syntax           |
|:---------------------|:-----------------|
| Command line         | `--log`          |
| YAML                 | `Log`            |
| Environment variable | `KURRENTDB_LOG`  |

For example, adding this line to the `kurrentdb.conf` file will force writing logs to
the `/tmp/kurrentdb/logs` directory:

```text:no-line-numbers
Log: /tmp/kurrentdb/logs
```

## Log level

You can change the level using the `LogLevel` setting:

| Format               | Syntax                 |
|:---------------------|:-----------------------|
| Command line         | `--log-level`          |
| YAML                 | `LogLevel`             |
| Environment variable | `KURRENTDB_LOG_LEVEL`  |

Acceptable values are: `Default`, `Verbose`, `Debug`, `Information`, `Warning`, `Error`, and `Fatal`.

Additionally: `Trace` is equivalent to `Verbose`, `Critical` is equivalent to `Fatal`, and `None` is equivalent to `Fatal`.

## Logging options

You can tune the KurrentDB logging further by using the logging options described below.

### Log configuration file

Specifies the location of the file which configures the logging levels of various components.

| Format               | Syntax                  |
|:---------------------|:------------------------|
| Command line         | `--log-config`          |
| YAML                 | `LogConfig`             |
| Environment variable | `KURRENTDB_LOG_CONFIG`  |

By default, the application directory (and `/etc/kurrentdb` on Linux and Mac) are checked. You may specify a
full path.

### HTTP requests logging

KurrentDB can also log all the incoming HTTP requests, like many HTTP servers do. Requests are logged
before being processed, so unsuccessful requests are logged too.

Use one of the following ways to enable the HTTP requests logging:

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--log-http-requests`          |
| YAML                 | `LogHttpRequests`              |
| Environment variable | `KURRENTDB_LOG_HTTP_REQUESTS`  |

**Default**: `false`, logging HTTP requests is disabled by default.

### Log failed authentication

For security monitoring, you can enable logging failed authentication attempts by
setting `LogFailedAuthenticationAttempts` setting to true.

| Format               | Syntax                                          |
|:---------------------|:------------------------------------------------|
| Command line         | `--log-failed-authentication-attempts`          |
| YAML                 | `LogFailedAuthenticationAttempts`               |
| Environment variable | `KURRENTDB_LOG_FAILED_AUTHENTICATION_ATTEMPTS`  |

**Default**: `false`

### Log console format

The format of the console logger. Use `Json` for structured log output.

| Format               | Syntax                          |
|:---------------------|:--------------------------------|
| Command line         | `--log-console-format`          |
| YAML                 | `LogConsoleFormat`              |
| Environment variable | `KURRENTDB_LOG_CONSOLE_FORMAT`  |

Acceptable values are: `Plain`, `Json`

**Default**: `Plain`

### Log file size

The maximum size of each log file, in bytes.

| Format               | Syntax                     |
|:---------------------|:---------------------------|
| Command line         | `--log-file-size`          |
| YAML                 | `LogFileSize`              |
| Environment variable | `KURRENTDB_LOG_FILE_SIZE`  |

**Default**: `1GB`

### Log file interval

How often to rotate logs.

| Format               | Syntax                         |
|:---------------------|:-------------------------------|
| Command line         | `--log-file-interval`          |
| YAML                 | `LogFileInterval`              |
| Environment variable | `KURRENTDB_LOG_FILE_INTERVAL`  |

Acceptable values are: `Minute`, `Hour`, `Day`, `Week`, `Month`, `Year`

**Default**: `Day`

### Log file retention count

Defines how many log files need to be kept on disk. By default, logs for the last month are available. Tune this setting if you need to have more history in the logs, or you need to save disk space. 

| Format               | Syntax                           |
|:---------------------|:---------------------------------|
| Command line         | `--log-file-retention-count`     |
| YAML                 | `LogFileRetentionCount`          |
| Environment variable | `KURRENTDB_LOG_RETENTION_COUNT`  |

**Default**: `31`

#### Disable log file

You can completely disable logging to a file by changing the `DisableLogFile` option.

| Format               | Syntax                        |
|:---------------------|:------------------------------|
| Command line         | `--disable-log-file`          |
| YAML                 | `DisableLogFile`              |
| Environment variable | `KURRENTDB_DISABLE_LOG_FILE`  |

**Default**: `false`

## Logs download

<Badge type="info" vertical="middle" text="License Required"/>

The _Logs Download_ feature provides HTTP access to KurrentDB logs so that they can be viewed without requiring file system access.

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

::: tip
You can use this API to download log files from your managed KurrentDB clusters in Kurrent Cloud.
:::

On startup the server will log a message similar to:
```:no-line-numbers
LogsEndpoint: Serving logs from "<FULL-PATH-TO-LOGS>" at endpoint "/admin/logs"
```

Access the logs via the `/admin/logs` endpoint on your server. Construct the full URL as follows:

```:no-line-numbers
http(s)://<node ip or hostname>:<node port>/admin/logs
```

Example:
```:no-line-numbers
https://localhost:2113/admin/logs
```

Only authenticated users belonging to the `$admins` or `$ops` groups can use this endpoint.

### Listing log files

To list the current log files, issue a `GET` to the `/admin/logs` endpoint

Example:
```bash:no-line-numbers
curl https://user:password@localhost:2113/admin/logs | jq
```

Sample response:
```json
[
  {
    "name": "log-stats20240205.json",
    "lastModified": "2024-02-05T13:14:14.2789475+00:00",
    "size": 1058614
  },
  {
    "name": "log20240205.json",
    "lastModified": "2024-02-05T13:14:37.0781601+00:00",
    "size": 158542
  }
]
```

The response is ordered from most recent change first, is limited to a maximum of 1000 items, and includes:

| Name         | Description                                                                                                                                                                                                                                       |
|--------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| name         | File name of the log file                                                                                                                                                                                                                         |
| createdAt    | Timestamp of when the log file was created in [round-trip format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) (local time with time zone information) |
| lastModified | Timestamp of the last modification in [round-trip format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) (local time with time zone information)         |
| size         | Size of the log file in bytes                                                                                                                                                                                                                     |

### Downloading log files

To download a specific log file, append its name to the URL:

Example:
```bash:no-line-numbers
curl https://user:password@localhost:2113/admin/logs/log20240205.json --output log20240205.json
```

### Troubleshooting

- **404 Not Found:** Verify that you have a valid license key.

- **401 Unauthorized:** Confirm the credentials are correct and the user belongs to the `$ops` or `$admins` group.
