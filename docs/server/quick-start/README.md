---
order: 1
headerDepth: 3
dir:
  text: "Quick Start"
  expanded: true
  order: 1
---

# Introduction

Welcome to the KurrentDB documentation.

KurrentDB is a database that's engineered for modern software applications and event-driven architectures. Its event-native design simplifies data modeling and preserves data integrity while the integrated streaming engine solves distributed messaging challenges and ensures data consistency.

KurrentDB is licensed under [Kurrent License v1 (KLv1)](https://github.com/EventStore/EventStore/blob/master/LICENSE.md), meaning that anyone can access and use it, but enterprise features are only enabled with a valid [license key](./installation.md#license-keys).

::: note
Although the source code for KurrentDB is available to view, the [KLv1 license](https://github.com/EventStore/EventStore/blob/master/LICENSE.md) is not an OSI-approved Open Source License.
:::

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
