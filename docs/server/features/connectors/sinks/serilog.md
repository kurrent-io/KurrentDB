---
title: 'Serilog Sink'
order: 6
---

## Overview

The Serilog sink logs detailed messages about the connector and record details.

The Serilog sink supports writing logs to the following outputs:

- [Console](https://github.com/serilog/serilog-sinks-console)
- [File](https://github.com/serilog/serilog-sinks-file)
- [Seq](https://github.com/serilog/serilog-sinks-seq)

These outputs can be configured using the Serilog settings in the configuration file.

## Quickstart

You can create the Serilog Sink connector as follows:

::: tabs
@tab Powershell

```powershell
$JSON = @"
{
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -d $JSON `
  http://localhost:2113/connectors/serilog-sink-connector
```

@tab Bash

```bash
JSON='{
  "settings": {
    "instanceTypeName": "serilog-sink",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "example-stream"
  }
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -d "$JSON" \
  http://localhost:2113/connectors/serilog-sink-connector
```

:::

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
| `configuration`     | **Type**: string<br><br>**Description:** The JSON serilog configuration.<br><br>**Accepted Values:** A base64 encoded string of the serilog configuration<br><br>**Default**: If not provided, it will write to the console |
| `includeRecordData` | **Type**: bool<br><br>**Description:** Whether the record data should be included in the log output.<br><br>**Default**: `true`                                                                                             |

## Write to Seq

1. Refer to the [Seq documentation](https://docs.datalust.co/docs/getting-started) for installation instructions.

2. Configure the Serilog sink to export logs to [Seq](https://datalust.co/seq) by following these steps:

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

3. Browse the Seq UI at `http://localhost:5341` to view the logs.

Follow the configuration guide from [Serilog Seq](https://github.com/serilog/serilog-sinks-seq) for more details.

## Write to a File

Encode the JSON configuration to base64, then provide the base64 encoded configuration to the Serilog sink connector.

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

This will write logs to `/tmp/logs/log.txt`.

Follow the configuration guide from [Serilog File](https://github.com/serilog/serilog-sinks-file) for more details.