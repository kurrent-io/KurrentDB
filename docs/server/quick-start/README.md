---
order: 1
headerDepth: 3
dir:
  text: "Quick Start"
  expanded: true
  order: 1
---

# Introduction

Welcome to the EventStoreDB documentation.

EventStoreDB is a database designed for [Event Sourcing](https://eventstore.com/blog/what-is-event-sourcing/). This documentation introduces key concepts of EventStoreDB and explains its installation, configuration, and operational concerns.

EventStoreDB v24.6 is available in both an Open-Source and a Commercial version:

- EventStoreDB OSS is the [open-source](https://github.com/EventStore/EventStore/tree/release/oss-v24.6) and free-to-use edition of EventStoreDB.
- EventStoreDB Commercial is available for customers with an EventStoreDB [paid support subscription](https://eventstore.com/support/). EventStoreDB Commercial adds enterprise-focused features such as LDAP and X.509 authentication, OpenTelemetry Exporter, correlation event sequence visualisation, and management CLI tool.

::: note
Although version 23.10 is licensed under the Event Store License based on BSD 3-Clause, starting from version 24.10, EventStoreDB will be licensed under the [Event Store License v2 (ESLv2)](https://github.com/EventStore/EventStore/blob/4cab8ca81a63f0a8f708d5564ea459fe5a7131de/LICENSE.md), which is not an OSI-approved Open Source License.
:::

## What's new

Find out [what's new](whatsnew.md) in this release to get details on new features and other changes.

## Getting started

Check the [getting started guide](/getting-started.md) for resources on the principles of EventStoreDB, Event Sourcing, database installation guidelines and choosing a client SDK.

## Support

### EventStoreDB community

Users of EventStoreDB OSS can use the [community forum](https://discuss.eventstore.com) for questions, discussions and getting help from community members.

### Enterprise customers

Customers with the paid [support plan](https://eventstore.com/support/) can open tickets using the [support portal](https://eventstore.freshdesk.com).

### Issues

We track most of the issues openly in the EventStoreDB [repository on GitHub](https://github.com/EventStore/EventStore). Before opening an issue, please ensure that a similar issue hasn't been opened already. Also, try searching closed issues that might contain a solution or workaround for your problem.

When opening an issue, follow our [guidelines](https://github.com/EventStore/EventStore/blob/master/CONTRIBUTING.md) for bug reports and feature requests. By doing so, you will greatly help us to solve your concerns most efficiently.

## Protocols, clients, and SDKs

EventStoreDB supports one client protocol, which is described below. The older TCP client API has been deprecated in version 20.2 and removed in version 24.2. The final version with TCP API support is 23.10. More information can be found in our [blog post](https://www.eventstore.com/blog/sunsetting-eventstoredb-tcp-based-client-protocol).

Since version 24.6, the legacy protocol is available as a [commercial plugin](../configuration/networking.md#external-tcp) available for Event Store customers.

### Client protocol

The client protocol is based on [open standards](https://grpc.io/) and is widely supported by many programming languages. EventStoreDB uses gRPC to communicate between the cluster nodes as well as for client-server communication.

When developing software that uses EventStoreDB, we recommend using one of the official SDKs.

#### EventStoreDB supported clients

- Python: [pyeventsourcing/esdbclient](https://pypi.org/project/esdbclient/)
- Node.js (JavaScript/TypeScript): [EventStore/EventStore-Client-NodeJS](https://github.com/EventStore/EventStore-Client-NodeJS)
- Java: [(EventStore/EventStoreDB-Client-Java](https://github.com/EventStore/EventStoreDB-Client-Java)
- .NET: [EventStore/EventStore-Client-Dotnet](https://github.com/EventStore/EventStore-Client-Dotnet)
- Go: [EventStore/EventStore-Client-Go](https://github.com/EventStore/EventStore-Client-Go)
- Rust: [EventStore/EventStoreDB-Client-Rust](https://github.com/EventStore/EventStoreDB-Client-Rust)

Read more in the [gRPC clients documentation](@clients/grpc/README.md).

#### Community developed clients

- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
- [Elixir (NFIBrokerage/spear)](https://github.com/NFIBrokerage/spear)

### HTTP

EventStoreDB also offers an HTTP-based interface. It consists of the REST-oriented API, and a realtime subscription feature based on the [AtomPub protocol](https://datatracker.ietf.org/doc/html/rfc5023). As it operates over HTTP, this is less efficient, but nearly every environment supports it.

Find out more about configuring the HTTP protocol on the [HTTP configuration](../configuration/networking.md#http-configuration) page.

::: warning Deprecation Note
The current AtomPub-based HTTP application API is disabled by default since v20 of EventStoreDB. You can enable it by adding an [option](../configuration/networking.md#atompub) to the server configuration. Although we plan to remove AtomPub support from future server versions, the server management HTTP API will remain available.
You need to enable the AtomPub protocol to have a fully functioning database user interface.
:::

Learn more about the EventStoreDB HTTP interface in the [HTTP documentation](../http-api/README.md). 

#### Community developed clients

- [PHP (prooph/event-store-http-client)](https://github.com/prooph/event-store-http-client/)
- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
