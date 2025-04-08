# Logs Endpoint Plugin
This plugin allows access to the EventStoreDB log files by using an HTTP endpoint.

To enable the endpoint you will need to download the plugin and add it to the plugins folder in your EventStoreDB installation directory. EventStoreDB should use it the next time it starts up.

After succesfull installation the server should log a similar message like below:
```
[19676, 1,13:46:13.151,INF] LogsEndpoint: Serving logs from "<FULL-PATH-TO-LOGS>" at endpoint "/admin/logs"
```

The endpoint can be found at `/admin/logs` of the server, the full url can be constructed as:
```
http(s)://< node ip or hostname >:<node port>/admin/logs
```
For example like this:
```
https://localhost:2113/admin/logs
```

## Access Restrictions

Usage of the endpoint is restricted to authenticated users who are either in the `$admins` or `$ops` groups.

## Usage

The functionality of the endpoint can be tested with a utility such as `curl` and optionally `jq` to improve displaying the endpoint output.

### List the Log Files

To display the current log files run:
```bash
curl https://<USER>:<PASSWORD>@hostname-or-ip:2113/admin/logs | jq
```
For example like this:
```bash

curl https://user:password@localhost:2113/admin/logs | jq
```

This should show something similar to:
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

The response is ordered as most recent change first and limited to a maximum of 1000 items and contains the following information:

| Name | Description |
|---|---|
| name | File name of the log file |
| createdAt | Timestamp of when the log file was created in [round-trip format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) (local time with time zone information) |
| lastModified | Timestamp of the last modification in [round-trip format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) (local time with time zone information) |
| size | Size of the log file in bytes |

### Downloading a Log File

Downloading a log file can be done by appending the name to the url:
```bash
curl https://<USER>:<PASSWORD>@hostname-or-ip:2113/admin/logs/<name> --output <name>
```
For example like this:
```bash
curl https://user:password@localhost:2113/admin/logs/log20240205.json --output log20240205.json
```

## Troubleshooting

## Plugin not loaded
The plugin has to be located in a subdirectory of the server's `plugin` directoy.
To check this:
1. Go to the installation directory of the server, the directory containing the EventStoreDb executable.
1. In this directory there should be a directoy called `plugins`, create it if this is not the case.
1. The `plugins` directory should have a subdirectoy for the plugin, for instance called `EventStore.Diagnostics.LogsEndpointPlugin` but this could be any name. Create it if it doesn't exist.
1. The binaries of the plugin should be located in the subdirectory which was checked in the previous step.

## Requests to endpoint return 404 not found
This could be caused by the endpoint not being loaded, check the server startup logs if this is the case.

## Requests to endpoint return 401 unauthorized
Make sure the used credentials are correct and the user is in either the `$ops` or `$admins` group.

## Failed to find the log files directory
It is possible the plugin is using a wrong directory for the logs when using a custom `NodeIp` and/or `NodePort`.
The directory for the logs is constructed with the IP and port of the server.
Make sure these are set with the latest `NodeIp` and `NodePort` settings and not the deprecated equivalents `HttpIp` or `HttpPort`.
