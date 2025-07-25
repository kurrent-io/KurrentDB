---
order: 1
headerDepth: 3
dir:
  text: "Quick Start"
  expanded: true
  order: 1
---

# Introduction

Welcome to the EventStoreDB 23.10 documentation.

EventStoreDB is a database designed for [Event Sourcing](https://eventstore.com/blog/what-is-event-sourcing/). This documentation introduces key concepts of EventStoreDB and explains its installation, configuration and operational concerns.

EventStoreDB v23.10 is available in both Open-Source and Commercial version:

- EventStoreDB OSS is the [open-source](https://github.com/EventStore/EventStore/tree/release/oss-v23.10) and free-to-use edition of EventStoreDB.
- EventStoreDB Commercial is available for customers with an EventStoreDB [paid support subscription](https://eventstore.com/support/). EventStoreDB Commercial adds enterprise-focused features such as LDAP integration, correlation event sequence visualisation, and management CLI.

::: note
Although version 23.10 is licensed under the Event Store License based on BSD 3-Clause, starting from version 24.10, EventStoreDB will be licensed under the [Event Store License v2 (ESLv2)](https://github.com/EventStore/EventStore/blob/4cab8ca81a63f0a8f708d5564ea459fe5a7131de/LICENSE.md), which is not an OSI-approved Open Source License.
:::

## Getting started

Get started by learning more about the principles of EventStoreDB, Event Sourcing, database installation guidelines and choosing a [client SDK](#protocols-clients-and-sdks).

## Support

### EventStoreDB community

Users of the OSS version of EventStoreDB can use the [community forum](https://discuss.eventstore.com) for questions, discussions and getting help from community members.

### Enterprise customers

Customers with the paid [support plan](https://eventstore.com/support/) can open tickets using the [support portal](https://eventstore.freshdesk.com).

### Issues

We track most of the issues openly in the EventStoreDB [repository on GitHub](https://github.com/EventStore/EventStore). Before opening an issue, please ensure that a similar issue hasn't been opened already. Also, try searching closed issues that might contain a solution or workaround for your problem.

When opening an issue, follow our [guidelines](https://github.com/EventStore/EventStore/blob/master/CONTRIBUTING.md) for bug reports and feature requests. By doing so, you would greatly help us to solve your concerns most efficiently.

## Protocols, clients, and SDKs

This getting started guide shows you how to get started with EventStoreDB by setting up an instance or cluster and configuring it.

EventStoreDB supports two protocols: gRPC and TCP, described below.

### gRPC protocol

The gRPC protocol is based on [open standards](https://grpc.io/) and is widely supported by many programming languages. EventStoreDB uses gRPC to communicate between the cluster nodes as well as for client-server communication.

We recommend using gRPC since it is the primary protocol for EventStoreDB moving forward. When developing software that uses EventStoreDB, we recommend using one of the official SDKs.

#### EventStoreDB supported clients

- Python: [pyeventsourcing/esdbclient](https://pypi.org/project/esdbclient/)
- Node.js (javascript/typescript): [EventStore/EventStore-Client-NodeJS](https://github.com/EventStore/EventStore-Client-NodeJS)
- Java: [(EventStore/EventStoreDB-Client-Java](https://github.com/EventStore/EventStoreDB-Client-Java)
- .NET: [EventStore/EventStore-Client-Dotnet](https://github.com/EventStore/EventStore-Client-Dotnet)
- Go: [EventStore/EventStore-Client-Go](https://github.com/EventStore/EventStore-Client-Go)
- Rust: [EventStore/EventStoreDB-Client-Rust](https://github.com/EventStore/EventStoreDB-Client-Rust)

Read more in the [gRPC clients documentation](@clients/grpc/README.md).

#### Community developed clients

- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
- [Elixir (NFIBrokerage/spear)](https://github.com/NFIBrokerage/spear)

### Legacy TCP protocol (support ends with 23.10 LTS)

EventStoreDB offers a low-level protocol in the form of an asynchronous TCP protocol that exchanges protobuf objects. At present this protocol has adapters for .NET and the JVM.

::: warning Deprecation Note
TCP protocol will be available only through version 23.10. Please plan to migrate your applications that use the TCP client SDK to use the gRPC SDK instead.
:::

Find out more about configuring the TCP protocol on the [TCP configuration](../configuration/networking.md#tcp-configuration) page.

#### EventStoreDB supported clients

- [.NET Framework and .NET Core](http://www.nuget.org/packages/EventStore.Client)

#### Community supported clients

Community supported clients are developed and maintained by community members, not Event Store staff. Feel free to open issues and PRs, when possible, in the client's GitHub repository. **The following clients which use TCP protocol, will not be compatible with Event Store server versions after 23.10.**

- [Node.js (x-cubed/event-store-client)](https://github.com/x-cubed/event-store-client)
- [Node.js (nicdex/node-eventstore-client)](https://github.com/nicdex/node-eventstore-client)
- [Elixir (exponentially/extreme)](https://github.com/exponentially/extreme)
- [Java 8 (msemys/esjc)](https://github.com/msemys/esjc)
- [Maven plugin (fuinorg/event-store-maven-plugin)](https://github.com/fuinorg/event-store-maven-plugin) (archived)
- [Go (jdextraze/go-gesclient)](https://github.com/jdextraze/go-gesclient)
- [PHP (prooph/event-store-client)](https://github.com/prooph/event-store-client/)
- [JVM Client (EventStore/EventStore.JVM)](https://github.com/EventStore/EventStore.JVM)
- [Haskell (EventStore/EventStoreDB-Client-Haskell)](https://github.com/EventStore/EventStoreDB-Client-Haskell)

### HTTP

EventStoreDB also offers an HTTP-based interface, based specifically on the [AtomPub protocol](https://datatracker.ietf.org/doc/html/rfc5023). As it operates over HTTP, this is less efficient, but nearly every environment supports it.

Find out more about configuring the HTTP protocol on the [HTTP configuration](../configuration/networking.md#http-configuration) page.

::: warning Deprecation Note
The current AtomPub-based HTTP application API is disabled by default since v20 of EventStoreDB. You can enable it by adding an [option](../configuration/networking.md#atompub) to the server configuration. Although we plan to remove AtomPub support from future server versions, the server management HTTP API will remain available.
:::

As the AtomPub protocol doesn't get any changes, you can use the v5 [HTTP API documentation](../http-api/introduction.md) for it.

#### Community developed clients

- [PHP (prooph/event-store-http-client)](https://github.com/prooph/event-store-http-client/)
- [Ruby (yousty/event_store_client)](https://github.com/yousty/event_store_client)
