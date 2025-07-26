---
title: "Installation"
---

## Quick start

EventStoreDB can run as a single node or as a highly-available cluster. For the cluster deployment, you'd need
three server nodes.

The installation procedure consists of the following steps:

- Create a configuration file for each cluster node.
- Install EventStoreDB on each node using one of the available methods.
- Obtain SSL certificates, either signed by a publicly trusted or private certificate authority.
- Copy the configuration files and SSL certificates to each node.
- Start the EventStoreDB service on each node.
- Check the cluster status using the Admin UI on any node.

### Default access

| User  | Password |
|-------|----------|
| admin | changeit |
| ops   | changeit |

### Configuration Wizard

The [EventStore Configurator](https://configurator.eventstore.com) is an online tool that can help you to go through all the
required steps.

You can provide the details about your desired deployment topology, and get the following:

- Generated configuration files
- Instructions for obtaining or generating SSL certificates
- Installation guidelines
- Client connection details

::: tip Event Store Cloud
You can avoid deploying, configuring, and maintaining the EventStoreDB instance yourself by
using [Event Store Cloud](https://www.eventstore.com/event-store-cloud).
:::

## Linux

### Install from PackageCloud

EventStoreDB has
pre-built [packages available for Debian-based distributions](https://packagecloud.io/EventStore/EventStore-OSS)
, [manual instructions for distributions that use RPM](https://packagecloud.io/EventStore/EventStore-OSS/install#bash-rpm)
, or you can [build from source](https://github.com/EventStore/EventStore#linux). The final package name to
install is `eventstore-oss`.

If you installed from a pre-built package, the server is registered as a service. Therefore, you can start
EventStoreDB with:

```bash:no-line-numbers
sudo systemctl start eventstore
```

When you install the EventStoreDB package, the service doesn't start by default. This allows you to
change the configuration, located at `/etc/eventstore/eventstore.conf` and to prevent creating database and
index files in the default location.

::: warning
We recommend that when using Linux you set the 'open file limit' to a high number. The precise
value depends on your use case, but at least between `30,000` and `60,000`.
:::

### Building from source

You can also build EventStoreDB from source. Before doing that, you need to install the .NET 6 SDK. EventStoreDB packages have the .NET Runtime embedded, so you don't need to install anything except the EventStoreDB package.

### Uninstall

If you installed one of
the [pre-built packages for Debian based systems](https://packagecloud.io/EventStore/EventStore-OSS), you can
remove it with:

```bash:no-line-numbers
sudo apt-get purge eventstore-oss
```

This removes EventStoreDB completely, including any user settings.

If you built EventStoreDB from source, remove it by deleting the directory containing the source and build and manually removing any environment variables.

## Windows

::: warning
EventStoreDB doesn't install as a Windows service. You need to ensure that the server executable
starts automatically.
:::

### Install from Chocolatey

EventStoreDB has [Chocolatey packages](https://chocolatey.org/packages/eventstore-oss) available that you can
install with the following command with administrator permissions.

```powershell:no-line-numbers
choco install eventstore-oss
```

### Download the binaries

You can also [download](https://eventstore.com/downloads/) a binary, unzip the archive and run from the folder
location with administrator permissions.

The following command starts EventStoreDB in dev mode with the database stored at the path `./db` and the logs in `./logs`.
Read more about configuring the EventStoreDB server in the [Configuration section](configuration.md).

```powershell:no-line-numbers
EventStore.ClusterNode.exe --dev --db ./db --log ./logs
```

EventStoreDB runs in an administration context because it starts an HTTP server through `http.sys`. For
permanent or production instances, you need to provide an ACL such as:

```powershell:no-line-numbers
netsh http add urlacl url=http://+:2113/ user=DOMAIN\username
```

For more information, refer to
Microsoft's `add urlacl` [documentation](https://docs.microsoft.com/en-us/windows/win32/http/add-urlacl).

To build EventStoreDB from source, refer to the
EventStoreDB [GitHub repository](https://github.com/EventStore/EventStore#building-eventstoredb).

### Uninstall

If you installed EventStoreDB with Chocolatey, you can uninstall with:

```powershell:no-line-numbers
choco uninstall eventstore-oss
```

This removes the `eventstore-oss` Chocolatey package.

If you installed EventStoreDB by [downloading a binary](https://eventstore.com/downloads/), you can remove it
by:

- Deleting the `EventStore-OSS-Win-*` directory.
- Removing the directory from your PATH.

## Docker

You can run EventStoreDB in Docker container as a single node, using insecure mode. It is useful in most
cases to try out the product and for local development purposes.

It's also possible to run a three-node cluster with or without SSL using Docker Compose. Such a setup is
closer to what you'd run in production.

### Run with Docker

EventStoreDB has a Docker image available for any platform that supports Docker.

The following command will start the EventStoreDB node using default HTTP port, without security. You can then
connect to it using one of the clients and the `esdb://localhost:2113?tls=false` connection string. The Admin
UI will be accessible, but the Stream Browser won't work (as it needs AtomPub to be enabled).

```bash:no-line-numbers
docker run --name esdb-node -it -p 2113:2113 -p 1113:1113 \
    eventstore/eventstore:latest --insecure --run-projections=All
```

If you want to start the node with legacy protocols enabled (TCP and AtomPub), you need to add a couple of
other options:

```bash:no-line-numbers
docker run --name esdb-node -it -p 2113:2113 -p 1113:1113 \
    eventstore/eventstore:latest --insecure --run-projections=All \
    --enable-external-tcp --enable-atom-pub-over-http
```

The command above would run EventStoreDB as a single node without SSL and with the legacy TCP protocol
enabled, so you can try out your existing apps with the latest database version.

Then, you'd be able to connect to EventStoreDB with gRPC and TCP clients. Also, the Stream Browser will work
in the Admin UI.

In order to sustainably keep the data, we also recommend mapping the database and index volumes.

### Use Docker Compose

You can also run a single-node instance or a three-node secure cluster locally using Docker Compose.

#### Insecure single node

You can use Docker Compose to run EventStoreDB in the same setup as the `docker run` command mentioned before.

Create file `docker-compose.yaml` with following content:

@[code{curl}](@samples/server/docker-compose.yaml)

Run the instance:

```bash:no-line-numbers
docker compose up
```

The command above would run EventStoreDB as a single node without SSL and with the legacy TCP protocol
enabled. You also get AtomPub protocol enabled, so you can get the stream browser to work in the Admin UI.

#### Secure cluster

With Docker Compose, you can also run a three-node cluster with security enabled. That kind of setup is
something you'd expect to use in production.

Create file `docker-compose.yaml` with following content:

@[code{curl}](@samples/server/docker-compose-cluster.yaml)

Quite a few settings are shared between the nodes and we use the `env` file to avoid repeating those settings.
So, add the `vars.env` file to the same location:

@[code{curl}](@samples/server/vars.env)

Containers will use the shared volume using the local `./certs` directory for certificates. However, if you
let Docker to create the directory on startup, the container won't be able to get write access to it.
Therefore, you should create the `certs` directory manually. You only need to do it once.

```bash:no-line-numbers
mkdir certs
```

Now you are ready to start the cluster.

```bash:no-line-numbers
docker-compose up
```

Watching the log messages, you will see that after some time, the elections process completes. Then you're able to connect to each
node using the Admin UI. Nodes should be accessible on the loopback address (`127.0.0.1` or `localhost`) over
HTTP and TCP, using ports specified below:

| Node  | TCP port | HTTP port |
| :---- | :------- | :-------- |
| node1 | 1111     | 2111      |
| node2 | 1112     | 2112      |
| node3 | 1113     | 2113      |

You have to tell your client to use secure connection for both TCP and gRPC.

| Protocol | Connection string                                                                                     |
| :------- | :---------------------------------------------------------------------------------------------------- |
| TCP      | `GossipSeeds=localhost:1111,localhost:1112,localhost:1113;ValidateServer=False;UseSslConnection=True` |
| gRPC     | `esdb://localhost:2111,localhost:2112,localhost:2113?tls=true&tlsVerifyCert=false`                    |

As you might've noticed, both connection strings have a setting to disable the certificate validation (`ValidateServer=False` for `TCP` and `tlsVerifyCert=false` for `gRPC`). It would prevent the invalid certificate error since the cluster uses a private, auto-generated CA.

However, **we do not recommend using this setting in production**. Instead, you can either add the CA certificate to the trusted root CA store or instruct your application to use such a certificate. See the [security section](security.md#certificate-installation-on-a-client-environment) for detailed instructions.

## Compatibility notes

Depending on how your EventStoreDB instance is configured, some features might not work. Below are some features that are unavailable due to the specified options.

| Feature                       | Options impact                                                                                                                                                                          |
| :---------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Connection for TCP clients    | External TCP is disabled by default. You need to enable it explicitly by using the `EnableExternalTcp` option.                                                                          |
| Connection without SSL or TLS | EventStoreDB 20.6+ is secure by default. Your clients need to establish a secure connection, unless you use the `Insecure` option.                                                      |
| Authentication and ACLs       | When using the `Insecure` option for the server, all the security is disabled. The `Users` menu item is also disabled in the Admin UI.                                                  |
| Projections                   | Running projections is disabled by default and the `Projections` menu item is disabled in the Admin UI. You need to enable projections explicitly by using the `RunProjections` option. |
| AtomPub protocol              | In 20.6+, the AtomPub protocol is disabled by default. If you use this protocol, you have to explicitly enable it by using the `EnableAtomPubOverHttp` option.                          |
| Stream browser                | The stream browser feature in Admin UI depends on the AtomPub protocol and is greyed out by default. You need to enable AtomPub (previous line) to make the stream browser work.        |
