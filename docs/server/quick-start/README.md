---
order: 1
headerDepth: 3
dir:
  text: "Get Started"
  expanded: true
  order: 1
---

# Quickstart

KurrentDB is an event‑native database for modern applications and event‑driven architectures. It combines simple data modeling with a built-in streaming engine to guarantee consistency. Available both as self-hosted, as well as cloud offered. 

KurrentDB uses the [Kurrent License v1 (KLv1)](https://github.com/kurrent-io/KurrentDB/blob/master/LICENSE.md). It is freely accessible and usable, but enterprise features require a valid license key.

## Enterprise features

* [Auto-scavenge](../operations/auto-scavenge.md) - Fully automated scheduling and execution of scavenging across cluster nodes, which removes operational complexity by handling configuration, scheduling, and preventing simultaneous node scavenging.
* Connectors - Licensed connectors:
- [Apache Kafka](../features/connectors/sinks/kafka.md)
- [Elasticsearch](../features/connectors/sinks/elasticsearch.md)
- [MongoDB](../features/connectors/sinks/mongo.md)
- [RabbitMQ ](../features/connectors/sinks/rabbitmq.md)
* [Stream Policy Authorization](../security/user-authorization.md#stream-policy-authorization) - Replaces ACLs with category-wide policies, enabling immediate policy updates and easier data segregation by tenant/microservice Kurrent enterprise platform.
* [Encryption-at-rest](../security/README.md#encryption-at-rest) - Provides additional data protection against unauthorized access.
* Advanced authentication - [LDAPS](../security/user-authentication.md#ldap-authentication), and [OAuth](../security/user-authentication.md#oauth-authentication) authentication.
* Advanced Monitoring - [Logs](../diagnostics/logs.md#logs-download), and [OpenTelemetry](../diagnostics/integrations.md#opentelemetry-exporter) endpoints.
* Visualize tab - Allows visualization of event correlations.


## What's new

Find out [what's new](whatsnew.md) in this release to get details on new features and other changes.

## Getting started

Check the [getting started guide](/getting-started/) for an introduction to KurrentDB and its features, along with detailed instructions to quickly get started.

## Support

### Community

Users of KurrentDB can use the [community forum](https://www.kurrent.io/community) for questions, discussions, and getting help from community members.

### Enterprise customers

Customers with a paid subscription can open tickets using the [support portal](https://eventstore.freshdesk.com). For additional information on supported versions, users can access KurrentDB's [release and support schedule](../release-schedule/).

### Issues

We openly track most issues in the KurrentDB [repository on GitHub](https://github.com/EventStore/EventStore). Before opening an issue, please ensure that a similar issue hasn't been opened already. Also, try searching closed issues that might contain a solution or workaround for your problem.

When opening an issue, follow our [guidelines](https://github.com/EventStore/EventStore/blob/master/CONTRIBUTING.md) for bug reports and feature requests. By doing so, you will significantly help us to solve your concerns most efficiently.

## Protocols, clients, and SDKs

KurrentDB supports one client protocol, which is described below. The older TCP client API has been removed in KurrentDB. The final version with TCP API support is EventStoreDB version 23.10. More information can be found in our [blog post](https://www.kurrent.io/blog/sunsetting-eventstoredb-tcp-based-client-protocol).

The legacy protocol is available for Kurrent customers as a [licensed plugin](../configuration/networking.md#external-tcp).

### Client protocol

The client protocol is based on [open standards](https://grpc.io/) and is widely supported by many programming languages. KurrentDB uses gRPC to communicate between the cluster nodes and for client-server communication.

When developing software that uses KurrentDB, we recommend using one of the official SDKs.

#### Supported clients

- Python: [pyeventsourcing/esdbclient](https://pypi.org/project/esdbclient/)
- Node.js (JavaScript/TypeScript): [EventStore/EventStore-Client-NodeJS](https://github.com/EventStore/EventStore-Client-NodeJS)
- Java: [(EventStore/EventStoreDB-Client-Java](https://github.com/EventStore/EventStoreDB-Client-Java)
- .NET: [EventStore/EventStore-Client-Dotnet](https://github.com/EventStore/EventStore-Client-Dotnet)
- Go: [EventStore/EventStore-Client-Go](https://github.com/EventStore/EventStore-Client-Go)
- Rust: [EventStore/EventStoreDB-Client-Rust](https://github.com/EventStore/EventStoreDB-Client-Rust)

Read more in the [gRPC clients documentation](@clients/grpc/README.md).

#### Community-developed clients

- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
- [Elixir (NFIBrokerage/spear)](https://github.com/NFIBrokerage/spear)

### HTTP

KurrentDB also offers an HTTP-based interface that consists of the REST-oriented API and a real-time subscription feature based on the [AtomPub protocol](https://datatracker.ietf.org/doc/html/rfc5023). As it operates over HTTP, this is less efficient, but nearly every environment supports it.

Learn more about configuring the HTTP protocol on the [HTTP configuration](../configuration/networking.md#http-configuration) page.

::: warning Deprecation Note
The current AtomPub-based HTTP application API is disabled by default. You can enable it by adding an [option](../configuration/networking.md#atompub) to the server configuration. Although we plan to remove AtomPub support from future server versions, the server management HTTP API will remain available.
You need to enable the AtomPub protocol to have a fully functioning database user interface.
:::

Learn more about the KurrentDB HTTP interface in the [HTTP documentation](@clients/http-api/README.md). 

#### Community-developed clients

- [PHP (prooph/event-store-http-client)](https://github.com/prooph/event-store-http-client/)
- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
