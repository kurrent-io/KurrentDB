# Introduction

::: tip
The new generation of EventStoreDB starts from version 20.6.
Learn more about the latest version [here](/latest.html).
:::

Welcome to the EventStoreDB 5.0 documentation.
 
EventStoreDB is a database designed for [Event Sourcing](https://eventstore.com/blog/what-is-event-sourcing/). This documentation introduces key concepts of EventStoreDB and explains its installation, configuration and operational concerns.

EventStoreDB is available as both a Open-Source and an Enterprise versions:

- EventStoreDB OSS is the [open-source](https://github.com/EventStore/EventStore) and free to use edition of EventStoreDB.
- EventStoreDB Enterprise is available for customers with EventStoreDB [paid support subscription](https://eventstore.com/support/). EventStoreDB Enterprise adds enterprise-focused features such as LDAP integration, correlation event sequence visualisation and management CLI.

## Getting started

Get started by learning more about principles of EventStoreDB, Event Sourcing, database installation guidelines and choosing the [client SDK](#protocols-clients-and-sdks).

## Support

### EventStoreDB community

Users of the OSS version of EventStoreDB can use the [community forum](https://discuss.eventstore.com) for questions, discussions and getting help from community members.

### Enterprise customers

Customers with the paid [support plan](https://eventstore.com/support/) can open tickets using the [support portal](https://eventstore.freshdesk.com).

### Issues

Since EventStoreDB is the open-source product, we track most of the issues openly in the EventStoreDB [repository on GitHub](https://github.com/EventStore/EventStore). Before opening an issue, please ensure that a similar issue hasn't been opened already. Also, try searching closed issues that might contain a solution or workaround for your problem.

When opening an issue, follow our guidelines for bug reports and feature requests. By doing so, you would greatly help us to solve your concerns in the most efficient way.

## Protocols, clients and SDKs

This getting started guide shows you how to get started with EventStoreDB by setting up an instance or cluster and configuring it.

EventStoreDB supports two protocols: TCP and HTTP (Atom). When developing software that uses EventStoreDB, we recommend using one of the official SDKs.

### TCP protocol

EventStoreDB offers a low-level protocol in the form of an asynchronous TCP protocol that exchanges protobuf objects. At present this protocol has adapters for .NET and the JVM.

#### EventStoreDB supported clients

-   [.NET Framework and .NET Core](http://www.nuget.org/packages/EventStore.Client)
-   [JVM Client](https://github.com/EventStore/EventStore.JVM)

#### Community developed clients

-   [Node.js](https://www.npmjs.com/package/event-store-client)
-   [Node.js](https://www.npmjs.com/package/ges-client)
-   [Node.js](https://github.com/nicdex/eventstore-node)
-   [Haskell](https://github.com/YoEight/eventstore)
-   [Erlang](https://github.com/anakryiko/erles)
-   [F#](https://github.com/haf/EventStore.Client.FSharp)
-   [Elixir](https://github.com/exponentially/extreme)
-   [Java 8](https://github.com/msemys/esjc)
-   [Maven plugin](https://github.com/fuinorg/event-store-maven-plugin)
-   [Rust](https://github.com/YoEight/eventstore-rs)
-   [Go](https://github.com/jdextraze/go-gesclient)
-   [PHP](https://github.com/prooph/event-store-client/)

### HTTP

EventStoreDB also offers an HTTP-based interface, based specifically on the [AtomPub protocol](https://datatracker.ietf.org/doc/html/rfc5023). As it operates over HTTP, this is less efficient, but nearly every environment supports it.

#### EventStoreDB supported clients

-   [HTTP API](./http-api/introduction.md)

#### Community developed clients

-   [PHP](https://github.com/dbellettini/php-eventstore-client)
-   [PHP](https://github.com/prooph/event-store-http-client/)
-   [Python](https://github.com/madedotcom/atomicpuppy)
-   [Ruby](https://github.com/arkency/http_eventstore)
-   [Go](https://github.com/jetbasrawi/go.geteventstore)

If you have a client to add, click the 'Improve this Doc' link on the top right of the page to submit a pull request.

### Which to use?

Many factors go into the choice of which of the protocols (TCP vs. HTTP) to use. Both have their strengths and weaknesses.

#### TCP is faster

This speed especially applies to subscribers as events pushed to the subscriber, whereas with Atom the subscribers poll the head of the atom feed to check if new events are available. The difference can be as high as 2–3 times higher (sub 10ms for TCP, vs. seconds for Atom).

Also, the number of writes per second supported is often dramatically higher when using TCP. At the time of writing, standard EventStoreDB appliances can service around 2000 writes/second over HTTP compared to 15,000-20,000/second over TCP. This increase might be a deciding factor if you are in a high-performance environment.

#### AtomPub is more scalable for large numbers of subscribers

::: warning
We do not recommend developing new applications and systems that use the AtomPub feature of EventStoreDB as it is being deprecated in the core product. Take a look at the gRPC streaming in EventStoreDB 20.6+ instead.
:::

This scalability is due to the ability to use intermediary caching with Atom feeds. Most URIs returned by EventStoreDB point to immutable data and are infinitely cachable. Therefore, on a replay of a projection, much of the data required is likely available on a local or intermediary cache. This can also lead to lower network traffic.

Atom tends to operate better in a large heterogeneous environment where you have callers from different platforms. This is especially true if you have to integrate with different teams or external vendors. Atom is an industry standard and well-documented protocol whereas the TCP protocol is a custom protocol they would need to understand.

Most platforms have good existing tooling for Atom including feed readers. None of this tooling exists for analyzing traffic with the TCP protocol.

