# Installation

EventStoreDB is available on multiple platforms. Below you can find instructions for installing an EventStoreDB instance.

Refer to the [clustering documentation](cluster.md) to upgrade your deployment to a highly available cluster. Cluster consists of several EventStoreDB nodes, follow the guidelines from this section to install each node of the cluster.

After installing an EventStoreDB instance you'd need to set up its [networking](networking.md) so you can connect to it from other machines.

Follow the [security guidelines](security.md) to prepare your instance of cluster for production use.

Check the [configuration](configuration.md) page to find out how to configure your deployment.


## Linux

### Install from PackageCloud

EventStoreDB has pre-built [packages available for Debian-based distributions](https://packagecloud.io/EventStore/EventStore-OSS), [manual instructions for distributions that use RPM](https://packagecloud.io/EventStore/EventStore-OSS/install#bash-rpm), or you can [build from source](https://github.com/EventStore/EventStore#linux). The final package name to install is `eventstore-oss`.

If you installed from a pre-built package, the server gets registered as a service. Therefore, you can start EventStoreDB with:

```bash
sudo systemctl start eventstore
```

When you install the EventStoreDB package, the service doesn't start by default. It's done to allow you to change the configuration, located at _/etc/eventstore/eventstore.conf_ and to prevent creating database and index files in the default location.

::: warning
We recommend that when using Linux you set the 'open file limit' to a high number. The precise value depends on your use case, but at least between `30,000` and `60,000`.
:::

### Building from source

You can also build EventStoreDB on Linux from source. Before doing that, you need to install Mono. We recommend [Mono 5.16.0](https://www.mono-project.com/download/stable/), but other versions may also work. EventStoreDB packages have Mono embedded, so you don't need to install anything except the EventStoreDB package.

### Uninstall

If you installed one of the [pre-built packages for Debian based systems](https://packagecloud.io/EventStore/EventStore-OSS), you can remove it with:

```bash
sudo apt-get purge eventstore-oss
```

This removes EventStoreDB completely, including any user settings.

If you built EventStoreDB from source, remove it by deleting the directory containing the source and build, and manually removing any environment variables.

## Windows

The prerequisites for installing on Windows are:

- NET Framework 4.0+

### Install from Chocolatey

EventStoreDB has [Chocolatey packages](https://chocolatey.org/packages/eventstore-oss) available that you can install with the following command in an elevated terminal:

```powershell
choco install eventstore-oss
```

### Download the binaries

You can also [download](https://eventstore.com/downloads/) a binary, unzip the archive and run from the folder location with an administrator console.

The following command starts EventStoreDB with the database stored at the path _./db_ and the logs in _./logs_. Read mode about configuring the EventStoreDB server node in the [Configuration section](configuration.md).

```powershell
EventStore.ClusterNode.exe --db ./db --log ./logs
```

EventStoreDB runs in an administration context because it starts an HTTP server through `http.sys`. For permanent or production instances you need to provide an ACL such as:

```powershell
netsh http add urlacl url=http://+:2113/ user=DOMAIN\username
```

For more information, refer to Microsoft `add urlacl` [documentation](https://docs.microsoft.com/en-us/windows/win32/http/add-urlacl).

To build EventStoreDB from source, refer to the EventStoreDB [GitHub repository](https://github.com/EventStore/EventStore#windows).

### Uninstall

If you installed EventStoreDB with Chocolatey, you can uninstall with:

```powershell
choco uninstall eventstore-oss
```

This removes the `eventstore-oss` Chocolatey package.

If you installed EventStoreDB by [downloading a binary](https://eventstore.com/downloads/), you can remove it by:

* Deleting the `EventStore-OSS-Win-*` directory.
* Removing the directory from your PATH.

## Docker

<!-- **TODO: explain more about using Docker and Compose. Volume mappings and other relevant configuration** -->

### Run with Docker

EventStoreDB has a Docker image available for any platform that supports Docker.

Pull the Docker image:

```bash
docker pull eventstore/eventstore:release-5.0.11
```

Run the container:

```bash
docker run --name eventstore-node -it -p 2113:2113 -p 1113:1113 eventstore/eventstore:release-5.0.11
```

Refer to the [image overview](https://hub.docker.com/r/eventstore/eventstore/) for more information.

The container won't accept command line parameters to the server executable. We recommend configuring EventStoreDB using the configuration file and mapping it as a volume.

In order to sustainably keep the data, we also recommend mapping the database and index volumes.

### Use Docker Compose

EventStoreDB has a Docker image available for any platform that supports Docker. In order to save keystrokes it is possible to run EventStoreDB via Docker Compose.

Create file `docker-compose.yaml` with following content:

@[code{curl}](@samples/server/docker-compose.yaml)

Run the instance:

```bash
docker-compose up
```

The Compose file above runs EventStoreDB as a single instance.
