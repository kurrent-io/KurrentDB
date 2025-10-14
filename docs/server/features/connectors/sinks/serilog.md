---
title: 'Serilog Sink'
---

## Overview

The Serilog sink logs detailed messages about the connector and record details.

The Serilog sink supports writing logs to the following outputs:

- [Console](https://github.com/serilog/serilog-sinks-console)
- [File](https://github.com/serilog/serilog-sinks-file)
- [Seq](https://github.com/serilog/serilog-sinks-seq)

These outputs can be configured using the Serilog settings in the configuration file.

## Quickstart

You can create the Serilog Sink connector as follows. Replace `id` with a unique connector name or ID:

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
```

After creating and starting the serilog sink connector, every time an event is
appended to the `example-stream`, the serilog sink connector will print the
record to the console. You can find a list of available management API endpoints
in the [API Reference](../manage.md).

Here is an example:

```
78689502-9502-9502-9502-172978689502 example-stream@16437 eventType1
78699411-9411-9411-9411-172978699411 example-stream@18975 eventType2
78699614-9614-9614-9614-172978699614 example-stream@19142 eventType3
78699732-9732-9732-9732-172978699732 example-stream@21170 eventType4
78699899-9899-9899-9899-172978699899 example-stream@23711 eventType5
```

## Settings

Adjust these settings to specify the behavior and interaction of your serilog sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The serilog sink inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The Serilog sink can be configured with the following options:

| Name                | Details                                                                                                                                                                                                                     |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `configuration`     | **Description:**<br>The JSON serilog configuration.<br><br>**Accepted Values:** A base64 encoded string of the serilog configuration<br><br>**Default**: If not provided, it will write to the console |
| `includeRecordData` | **Description:**<br>Whether the record data should be included in the log output.<br><br>**Default**: `"true"`                                                                                             |

## Write to Seq

1. Refer to the [Seq documentation](https://docs.datalust.co/docs/getting-started) for installation instructions.

2. Configure the Serilog sink to export logs to [Seq](https://datalust.co/seq) by following these steps:

::: tabs
@tab Configuration

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Seq"],
    "WriteTo": [
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341",
          "payloadFormatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      }
    ]
  }
}
```

@tab cURL

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "configuration": "ewogICJTZXJpbG9nIjogewogICAgIlVzaW5nIjogWyJTZXJpbG9nLlNpbmtzLlNlcSJdLAogICAgIldyaXRlVG8iOiBbCiAgICAgIHsKICAgICAgICAiTmFtZSI6ICJTZXEiLAogICAgICAgICJBcmdzIjogewogICAgICAgICAgInNlcnZlclVybCI6ICJodHRwOi8vbG9jYWxob3N0OjUzNDEiLAogICAgICAgICAgInBheWxvYWRGb3JtYXR0ZXIiOiAiU2VyaWxvZy5Gb3JtYXR0aW5nLkNvbXBhY3QuQ29tcGFjdEpzb25Gb3JtYXR0ZXIsIFNlcmlsb2cuRm9ybWF0dGluZy5Db21wYWN0IgogICAgICAgIH0KICAgICAgfQogICAgXQogIH0KfQ=="
}
```

:::


3. Browse the Seq UI at `http://localhost:5341` to view the logs.

Follow the configuration guide from [Serilog Seq](https://github.com/serilog/serilog-sinks-seq) for more details.

## Write to a File

Encode the JSON configuration to base64, then provide the base64 encoded configuration to the Serilog sink connector.

::: tabs
@tab Configuration

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "/tmp/logs/log.txt",
          "rollingInterval": "Infinite"
        }
      }
    ]
  }
}
```

@tab cURL

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "configuration": "ewogICJTZXJpbG9nIjogewogICAgIldyaXRlVG8iOiBbCiAgICAgIHsKICAgICAgICAiTmFtZSI6ICJGaWxlIiwKICAgICAgICAiQXJncyI6IHsKICAgICAgICAgICJwYXRoIjogIi90bXAvbG9ncy9sb2cudHh0IiwKICAgICAgICAgICJyb2xsaW5nSW50ZXJ2YWwiOiAiSW5maW5pdGUiCiAgICAgICAgfQogICAgICB9CiAgICBdCiAgfQp9"
}
```

:::


This will write logs to `/tmp/logs/log.txt`.

Follow the configuration guide from [Serilog File](https://github.com/serilog/serilog-sinks-file) for more details.