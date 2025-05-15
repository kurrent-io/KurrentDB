# Metrics

KurrentDB connectors expose a variety of metrics to help you monitor the health,
performance, and reliability of your system. These metrics are organized by type
and can be used to track different aspects of your connectors.

## Usage

| Time series                      | Type                                               | Description                              |
| :------------------------------- | :------------------------------------------------- | :--------------------------------------- |
| `kurrent_connector_active_total` | [Gauge](../../diagnostics/metrics.md#common-types) | Current number of active data connectors |

## Sink

| Time series                                    | Type                                                   | Description                                                                  |
| :--------------------------------------------- | :----------------------------------------------------- | :--------------------------------------------------------------------------- |
| `kurrent_sink_written_total_records`           | [Histogram](../../diagnostics/metrics.md#common-types) | Total number of records successfully written to the sink                     |
| `kurrent_sink_errors_total`                    | [Counter](../../diagnostics/metrics.md#common-types)   | Total number of errors encountered during sink operations                    |
| `kurrent_sink_transform_duration_milliseconds` | [Histogram](../../diagnostics/metrics.md#common-types) | Duration of data transformation before writing to the sink (in milliseconds) |

## Consumer

| Time series                                      | Type                                                 | Description                                                         |
| :----------------------------------------------- | :--------------------------------------------------- | :------------------------------------------------------------------ |
| `messaging_kurrent_consumer_message_count_total` | [Counter](../../diagnostics/metrics.md#common-types) | Total number of messages consumed from the messaging system         |
| `messaging_kurrent_consumer_commit_latency`      | [Counter](../../diagnostics/metrics.md#common-types) | Total latency observed when committing message offsets              |
| `messaging_kurrent_consumer_lag`                 | [Gauge](../../diagnostics/metrics.md#common-types)   | Difference between the latest message and the last consumed message |

## Producer

| Time series                                                | Type                                                   | Description                                                              |
| :--------------------------------------------------------- | :----------------------------------------------------- | :----------------------------------------------------------------------- |
| `messaging_kurrent_producer_queue_length`                  | [Gauge](../../diagnostics/metrics.md#common-types)     | Current number of messages waiting in the producer queue                 |
| `messaging_kurrent_producer_message_count_total`           | [Counter](../../diagnostics/metrics.md#common-types)   | Total number of messages successfully produced to the messaging system   |
| `messaging_kurrent_producer_produce_duration_milliseconds` | [Histogram](../../diagnostics/metrics.md#common-types) | Time taken to produce messages to the messaging system (in milliseconds) |

## Processor

| Time series                                     | Type                                                 | Description                                                  |
| :---------------------------------------------- | :--------------------------------------------------- | :----------------------------------------------------------- |
| `messaging_kurrent_processor_error_count_total` | [Counter](../../diagnostics/metrics.md#common-types) | Total number of errors encountered during message processing |
