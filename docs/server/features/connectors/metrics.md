# Metrics

KurrentDB connectors expose a variety of metrics to help you monitor the health,
performance, and reliability of your system. These metrics are organized by type
and can be used to track different aspects of your connectors.

## Usage

| Time series                      | Type                                               | Description                              |
|:---------------------------------|:---------------------------------------------------|:-----------------------------------------|
| `kurrent_connector_active_total` | [Gauge](../../diagnostics/metrics.md#common-types) | Current number of active data connectors |

## Sink

| Time series                          | Type                                                   | Description                                                             |
|:-------------------------------------|:-------------------------------------------------------|:------------------------------------------------------------------------|
| `kurrent_sink_written_total_records` | [Histogram](../../diagnostics/metrics.md#common-types) | Total number of records successfully written to the sink                |
| `kurrent_sink_errors_total`          | [Counter](../../diagnostics/metrics.md#common-types)   | Total number of errors encountered during sink operations               |
| `kurrent_sink_transform_duration_s`  | [Histogram](../../diagnostics/metrics.md#common-types) | Duration of data transformation before writing to the sink (in seconds) |
| `kurrent_sink_write_latency_s`       | [Histogram](../../diagnostics/metrics.md#common-types) | Time between event creation and sink write acknowledgment (in seconds)  |

## Consumer

| Time series                                   | Type                                                   | Description                                                         |
|:----------------------------------------------|:-------------------------------------------------------|:--------------------------------------------------------------------|
| `messaging_kurrent_consumer_message_count`    | [Counter](../../diagnostics/metrics.md#common-types)   | Total number of messages consumed from the messaging system         |
| `messaging_kurrent_consumer_commit_latency_s` | [Histogram](../../diagnostics/metrics.md#common-types) | Time between receiving a record and committing its position (in seconds) |
| `messaging_kurrent_consumer_lag`              | [Gauge](../../diagnostics/metrics.md#common-types)     | Difference between the latest message and the last consumed message |

## Producer

| Time series                                     | Type                                                   | Description                                                            |
|:------------------------------------------------|:-------------------------------------------------------|:-----------------------------------------------------------------------|
| `messaging_kurrent_producer_queue_length`       | [Gauge](../../diagnostics/metrics.md#common-types)     | Current number of messages waiting in the producer queue               |
| `messaging_kurrent_producer_message_count`      | [Counter](../../diagnostics/metrics.md#common-types)   | Total number of messages successfully produced to the messaging system |
| `messaging_kurrent_producer_produce_duration_s` | [Histogram](../../diagnostics/metrics.md#common-types) | Time taken to produce messages to the messaging system (in seconds)    |

## Processor

| Time series                               | Type                                                 | Description                                                  |
|:------------------------------------------|:-----------------------------------------------------|:-------------------------------------------------------------|
| `messaging_kurrent_processor_error_count` | [Counter](../../diagnostics/metrics.md#common-types) | Total number of errors encountered during message processing |
