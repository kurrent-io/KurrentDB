# OTLP Exporter Plugin
This plugin enables users to export EventStoreDB metrics via the Open Telemetry Protocol to a designated endpoint. The users have the flexibility to set up a collector endpoint as per their requirements. For example, the user can configure an OTEL collector to receive, process, and export metrics as desired.
To read more on the OTEL collector, please visit the following URL:
https://opentelemetry.io/docs/collector/

To enable the OTLP Exporter, you will need to download the plugin and place it in the plugins folder in your EventStoreDB installation directory. EventStore should use it the next time it starts up.

After successful installation the server should log a similar message like below:
```
[ 9408, 1,12:47:10.982,INF] Loaded SubsystemsPlugin plugin: "otlp-exporter" "24.2.0.0".
```

Additionally, you also need to provide the json configuration file. Please ensure that the json configuration file appears inside the "config" folder within your EventStoreDB installation directory.
If the "config" folder does not exist, please create it and paste the file inside.

The configuration file allows you to specify the following options for the OTLP exporter:

| Name     | Description                                                 |
|----------|-------------------------------------------------------------|
| Endpoint | Target to which the OTLP exporter is going to send the data |
| Headers  | Optional headers for the connection.                        |

Headers is a key-value pair separated by commas. To provide custom headers, you can add the "Headers" option in the json configuration file and set the value according to your requirements. For example:
```
"Headers": "api-key=value,other-config-value=value"
```
In the above example, we are adding two custom key value pairs to the "Headers" option.

## Configuration File Format
Here is the template for the json configuration file:
```
{
  "EventStore": {
    "OpenTelemetry": {
      "Otlp": {
        "Endpoint": "http://localhost:4317",
        "Headers": ""
      }
    }
  }
}
```

## Usage
The plugin can be tested by running an OTEL collector instance in Docker and configuring any monitoring tool such as Prometheus/Jaeger/Zipkin which can scrape the data from your Endpoint.
```
EventStoreDB --> OTLP exporter --> OTEL collector --> Your choice of monitoring tool
```

The EventStoreDB logs the following message when the server start exporting metrics to the Endpoint:
```
[27448, 1,10:34:03.283,INF] OtlpExporter: Exporting metrics to http://localhost:4317/ every 15.0 seconds
```

In this scenario, the log entry above indicates that the server is exporting the metrics to localhost:4317 at intervals of 15 seconds. The scrape interval value can be configured through metricsconfig.json file located
in the server installation directory. By default, this interval is set to 15 seconds and can be modified by updating the corresponding entry in the metricsconfig.json file:
```
"ExpectedScrapeIntervalSeconds": 15
```

## Troubleshooting

### Plugin not loaded
The plugin has to be located in a subdirectory of the server's `plugin` directory.
To check this:
1. Go to the installation directory of the server, the directory containing the EventStoreDB executable.
2. In this directory there should be a directory called `plugins`, create it if this is not the case.
3. The `plugins` directory should have a subdirectory for the plugin, for instance called `EventStore.OtlpExporterPlugin` but this could be any name. Create it if it doesn't exist.
4. The binaries of the plugin should be located in the subdirectory which was checked in the previous step.

### EventStoreDB metrics not exported to endpoint
Make sure the configuration file exists in "config" folder with correct format. The plugin will not be able to push the metrics if the configuration file is missing or the format is incorrect. In this case, the EventStoreDB logs the message like below to notify the user:
```
[ 9408, 1,12:48:10.982,INF] OtlpExporter: No EventStore:OpenTelemetry:Otlp configuration found. Not exporting metrics.
```
