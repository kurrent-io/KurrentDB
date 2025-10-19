# Queries UI

<wbr><Badge type="info" vertical="middle" text="License Required"/>

The KurrentDB Web Admin UI provides a basic interface to write and run ad-hoc queries against your event streams. This is useful for quick analysis and exploration of your data without needing to set up a full projection.

::: important
The query feature is intended for temporary and exploratory use. For long-running or production workloads, consider using persistent projections or external tools.

KurrentDB currently does not have an advance query planning and is only able to use the default [secondary indexes](../indexes/secondary.md) for queries. Therefore, complex queries may not perform optimally.

When querying event data and metadata, keep in mind that the query engine processes events sequentially. For large datasets, this may lead to longer query execution times and emit a large number of read requests.
:::

