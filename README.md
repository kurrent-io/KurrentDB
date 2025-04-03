<a href="https://kurrent.io">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./KurrentLogo-White.png">
    <source media="(prefers-color-scheme: light)" srcset="./KurrentLogo-Black.png">
    <img alt="Kurrent" src="./KurrentLogo-Plum.png" height="50%" width="50%">
  </picture>
</a>

- [What is Kurrent](#what-is-kurrent)
- [What is KurrentDB](#what-is-kurrentdb)
- [What is Kurrent Cloud](#what-is-kurrent-cloud)
- [Licensing](#licensing)
- [Documentation](#docs)
- [Getting started with KurrentDB](#getting-started-with-kurrentdb)
- [Getting started with Kurrent Cloud](#getting-started-with-kurrent-cloud)
- [Client libraries](#client-libraries)
- [Deployment](#deployment)
- [Communities](#communities)
- [Contributing](#contributing)
- [Building KurrentDB](#building-kurrentdb)
- [More resources](#more-resources)

## What is Kurrent

Event Store – the company and the product – are rebranding as Kurrent.

- The flagship product will be referred to as “the Kurrent event-native data platform” or “the Kurrent platform” or simply “Kurrent"
- EventStoreDB will be referred to as KurrentDB
- Event Store Cloud will now be called Kurrent Cloud

Read more about the rebrand in the [rebrand FAQ](https://www.kurrent.io/blog/kurrent-re-brand-faq).

## What is KurrentDB

KurrentDB is a database that's engineered for modern software applications and event-driven architectures. Its event-native design simplifies data modeling and preserves data integrity while the integrated streaming engine solves distributed messaging challenges and ensures data consistency.

Download the [latest version](https://kurrent.io/downloads).
For more product information visit [the website](https://kurrent.io/kurrent).

## What is Kurrent Cloud?

 Kurrent Cloud is a fully managed cloud offering that's designed to make it easy for developers to build and run highly available and secure applications that incorporate KurrentDB without having to worry about managing the underlying infrastructure. You can provision KurrentDB clusters in AWS, Azure, and GCP, and connect these services securely to your own cloud resources.

For more details visit [the website](https://kurrent.io/kurrent-cloud).

## Licensing

View [KurrentDB's licensing information](https://github.com/EventStore/EventStore/blob/master/LICENSE.md).

## Docs

For guidance on installation, development, deployment, and administration, see the [User Documentation](https://docs.kurrent.io/).

## Getting started with KurrentDB

Follow the [getting started guide](https://docs.kurrent.io/latest.html).

## Getting started with Kurrent Cloud

Kurrent can manage KurrentDB for you, so you don't have to run your own clusters.
See the online documentation: [Getting started with Kurrent Cloud](https://docs.kurrent.io/cloud/).

## Client libraries

[This guide](https://docs.kurrent.io/clients/grpc/getting-started.html) shows you how to get started with KurrentDB by setting up an instance or cluster and configuring it.
KurrentDB supports the gRPC protocol.

### KurrentDB supported clients

- Python: [pyeventsourcing/esdbclient](https://pypi.org/project/esdbclient/)
- Node.js (javascript/typescript): [EventStore/EventStore-Client-NodeJS](https://github.com/EventStore/EventStore-Client-NodeJS)
- Java: [(EventStore/EventStoreDB-Client-Java](https://github.com/EventStore/EventStoreDB-Client-Java)
- .NET: [EventStore/EventStore-Client-Dotnet](https://github.com/EventStore/EventStore-Client-Dotnet)
- Go: [EventStore/EventStore-Client-Go](https://github.com/EventStore/EventStore-Client-Go)
- Rust: [EventStore/EventStoreDB-Client-Rust](https://github.com/EventStore/EventStoreDB-Client-Rust)
- Read more in the [gRPC clients documentation](https://docs.kurrent.io/clients/grpc)

### Community supported clients

- Elixir: [NFIBrokerage/spear](https://github.com/NFIBrokerage/spear)
- Ruby: [yousty/event_store_client](https://github.com/yousty/event_store_client)

Read more in the [documentation](https://docs.kurrent.io/server/latest/quick-start/#protocols-clients-and-sdks).

### Legacy clients (support ends with EventStoreDB v23.10 LTS)

- .NET: [EventStoreDB-Client-Dotnet-Legacy](https://github.com/EventStore/EventStoreDB-Client-Dotnet-Legacy)

## Deployment

- Kurrent Cloud - [steps to get started in Kurrent Cloud](https://docs.kurrent.io/cloud/).
- Self-managed - [steps to host KurrentDB yourself](https://docs.kurrent.io/latest/quick-start/installation).

## Communities

[Join our global community](https://www.kurrent.io/community) of developers.

- [Discuss](https://discuss.eventstore.com/)
- [Discord (Event Store)](https://discord.gg/Phn9pmCw3t)
- [Discord (ddd-cqrs-es)](https://discord.com/invite/sEZGSHNNbH)

## Contributing

Development is done on the `master` branch.
We attempt to do our best to ensure that the history remains clean and to do so, we generally ask contributors to squash their commits into a set or single logical commit.

If you want to switch to a particular release, you can check out the release branch for that particular release. For example:
`git checkout release/oss-v22.10`

- [Create an issue](https://github.com/EventStore/EventStore/issues)
- [Documentation](https://docs.kurrent.io/)
- [Contributing guide](https://github.com/EventStore/EventStore/blob/master/CONTRIBUTING.md)

## Building KurrentDB

KurrentDB is written in a mixture of C# and JavaScript. It can run on Windows, Linux and macOS (using Docker) using the .NET Core runtime.

**Prerequisites**

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

Once you've installed the prerequisites for your system, you can launch a `Release` build of KurrentDB as follows:

```
dotnet build -c Release src
```

The build scripts: `build.sh` and `build.ps1` are also available for Linux and Windows respectively to simplify the build process.

To start a single node, you can then run:

```
dotnet ./src/KurrentDB/bin/x64/Release/net8.0/KurrentDB.dll --dev --db ./tmp/data --index ./tmp/index --log ./tmp/log
```

### Running the tests

You can launch the tests as follows:

```
dotnet test src/EventStore.sln
```

### Build KurrentDB Docker image

You can also build a Docker image by running the command:

```
docker build --tag mykurrentdb . \
--build-arg CONTAINER_RUNTIME={container-runtime}
--build-arg RUNTIME={runtime}
```

For instance:

```
docker build --tag mykurrentdb . \
--build-arg CONTAINER_RUNTIME=bookworm-slim \
--build-arg RUNTIME=linux-x64
```

**_Note:_** Because of the [Docker issue](https://github.com/moby/buildkit/issues/1900), if you're building a Docker image on Windows, you may need to set the `DOCKER_BUILDKIT=0` environment variable. For instance, running in PowerShell:

```
$env:DOCKER_BUILDKIT=0; docker build --tag mykurrentdb . `
--build-arg CONTAINER_RUNTIME=bookworm-slim `
--build-arg RUNTIME=linux-x64
```

Currently, we support the following configurations:

1. Bookworm slim:

- `CONTAINER_RUNTIME=bookworm-slim`
- `RUNTIME=linux-x64`

2. Jammy:

- `CONTAINER_RUNTIME=Jammy`
- `RUNTIME=linux-x64`

3. Alpine:

- `CONTAINER_RUNTIME=alpine`
- `RUNTIME=linux-musl-x64`

You can verify the built image by running:

```
docker run --rm mykurrentdb --insecure --what-if
```

## More resources

- [Release notes](https://kurrent.io/blog/release-notes)
- [Beginners Guide to Event Sourcing](https://kurrent.io/event-sourcing)
- [Articles](https://kurrent.io/blog)
- [Webinars](https://kurrent.io/webinars)
- [Contact us](https://kurrent.io/contact)
