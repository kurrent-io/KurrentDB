---
title: "Installation"
order: 3
---

<CloudBanner />

## Quick start

EventStoreDB can run as a single node or as a highly-available cluster. For the cluster deployment, you'd need three server nodes.

The installation procedure consists of the following steps:

- Create a configuration file for each cluster node. If you are using any licensed features, ensure that you configure a [license key](#license-keys).
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

### License Keys

Some features of EventStoreDB require a license key to access. When you purchase an enterprise subscription, the license key will be sent to your company's designated license administrator. Existing customers who would like to upgrade to a 24.10+ enterprise license should contact their Kurrent (formerly Event Store) account manager or contact us [here](https://www.kurrent.io/talk_to_expert). As an existing customer, you can also try the enterprise features by signing up for a [free trial license key](https://www.kurrent.io/kurrent_free_trial).  

There are various ways to provide the license key to EventStoreDB. For more information, refer to the [configuration guide](../configuration/README.md).

Configuration file:

```yaml
Licensing:
  LicenseKey: Yourkey
```

Environment variable:

```
EVENTSTORE_LICENSING__LICENSE_KEY
```

For most features that require a license, EventStoreDB will not start if the feature is enabled but the license key is not provided or is invalid.

## Linux

EventStoreDB has pre-built packages available on Cloudsmith for RedHat or Debian-based distributions.
The name of the EventStoreDB package is `eventstoredb-ee`.

### Ubuntu packages

#### Distribution setup
To install packages, you can quickly set up the repository automatically (recommended):

```bash
curl -1sLf \
  'https://packages.eventstore.com/public/eventstore/setup.deb.sh' \
  | sudo -E bash
```

If you need to force a specific distribution, release/version, architecture, or component (if supported), you can also do that (e.g. if your system is compatible but not identical):

```bash
curl -1sLf \
  'https://packages.eventstore.com/public/eventstore/setup.deb.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH component=COMPONENT bash
```

Alternatively, you can manually configure it yourself before installing packages:

```bash
apt-get install -y debian-keyring  # debian only
apt-get install -y debian-archive-keyring  # debian only
apt-get install -y apt-transport-https
# For Debian Stretch, Ubuntu 16.04 and later
keyring_location=/usr/share/keyrings/eventstore-eventstore-archive-keyring.gpg
# For Debian Jessie, Ubuntu 15.10 and earlier
keyring_location=/etc/apt/trusted.gpg.d/eventstore-eventstore.gpg
curl -1sLf 'https://packages.eventstore.com/public/eventstore/gpg.D008FDA5E151E345.key' |  gpg --dearmor >> ${keyring_location}
curl -1sLf 'https://packages.eventstore.com/public/eventstore/config.deb.txt?distro=ubuntu&codename=zorin&component=main' > /etc/apt/sources.list.d/eventstore-eventstore.list
sudo chmod 644 ${keyring_location}
sudo chmod 644 /etc/apt/sources.list.d/eventstore-eventstore.list
apt-get update
```

#### Install with apt-get

Add the repository to your system according to the [instructions on Cloudsmith](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-deb).

Then, install the package:

```bash
apt-get install eventstoredb-ee=24.10.6
```

#### Uninstall with apt-get

You can uninstall the package with:

```bash
apt-get remove eventstoredb-ee
```

If you want to also remove any configuration files and user settings, use:

```bash
apt-get purge eventstoredb-ee
```

### RedHat packages

#### Distribution setup

To install packages, you can quickly set up the repository automatically (recommended):

```bash
curl -1sLf \
  'https://packages.eventstore.com/public/eventstore/setup.rpm.sh' \
  | sudo -E bash
```

If you need to force a specific distribution, release/version, or architecture, you can also do that (e.g. if your system is compatible but not identical):

```bash
curl -1sLf \
  'https://packages.eventstore.com/public/eventstore/setup.rpm.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH bash
```

Alternatively, you can manually configure it yourself before installing packages:

```bash
yum install yum-utils pygpgme
rpm --import 'https://packages.eventstore.com/public/eventstore/gpg.D008FDA5E151E345.key'
curl -1sLf 'https://packages.eventstore.com/public/eventstore/config.rpm.txt?distro=el&codename=9' > /tmp/eventstore-eventstore.repo
yum-config-manager --add-repo '/tmp/eventstore-eventstore.repo'
yum -q makecache -y --disablerepo='*' --enablerepo='eventstore-eventstore'
```

::: note
Please replace el and 7 above with your actual distribution and version and use wildcards when enabling multiple repos.
:::

#### Install with yum

Add the repository to your system according to the [instructions on Cloudsmith](https://cloudsmith.io/~eventstore/repos/eventstore/setup/#formats-rpm).

Then, install the package:

```bash
yum install eventstoredb-ee-24.10.6-1.x86_64
```

#### Uninstall with yum

You can uninstall the package with:

```bash
yum remove eventstoredb-ee
```

### Running the eventstore service

Once installed, the server is registered as a service. Therefore, you can start EventStoreDB with:

```bash
systemctl start eventstore
```

When you install the EventStoreDB package, the service doesn't start by default. This allows you to change the configuration located at `etc/eventstore/eventstore.conf` and to prevent creating database and index files in the default location.

::: warning
We recommend that when using Linux you set the 'open file limit' to a high number. The precise value depends on your use case, but at least between `30,000` and `60,000`.
:::

## Windows

### NuGet

EventStoreDB has NuGet packages available on Cloudsmith, which replaces the previous Chocolatey packages.

Add a new package source to your Chocolatey configuration:

```powershell
choco source add -n eventstore-eventstore -s https://nuget.eventstore.com/eventstore/v2/
```

#### Install with Chocolatey

You can install EventStoreDB through Chocolatey:

```powershell
choco install eventstoredb-ee -s eventstore-eventstore --version 24.10.6
```

EventStoreDB can then be run with `EventStore.ClusterNode.exe`:

```powershell
EventStore.ClusterNode.exe --config {your config file}
```

#### Uninstall with Chocolatey

You can uninstall EventStoreDB through Chocolatey with:

```powershell
choco uninstall eventstoredb-ee
```

#### Running as a service

EventStoreDB 24.10.6+ can be run as a windows service. The following commands are for example. Consult the [sc.exe documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config) for details. 

Create the service:

```powershell
sc.exe create "EventStoreDB" `
  start= delayed-auto        `
  binpath= "C:\ProgramData\chocolatey\lib\eventstoredb-ee\eventstoredb-24.10.6-ee-windows.x64\EventStore.ClusterNode.exe --config=c:\path\to\eventstore-config.yaml"
```

Configure the restart policy:

```powershell
sc.exe failure "EventStoreDB" `
  reset= 0                    `
  actions= restart/5000/restart/5000/restart/5000
```

Start the service:

```powershell
sc.exe start "EventStoreDB"
```

Logs will still be written to the usual log file location. By default on windows this is the `logs` directory within the directory that contains `EventStore.ClusterNode.exe`.

::: warning
In this version the the service restart policy does not guarantee that the service is running. If running as a Windows Service, we recommend configuring
the Windows Task Scheduler to start the service regularly (say, every 10 seconds), which will do nothing if the service is already running.

This is for two reasons, both of which we intend to address in a future version:

1. If the service shuts down gracefully, such as when going offline for truncation, it will be not be restarted by the policy.
2. If the service does not start within 30s (by default), it will not be restarted by the policy. The 'start' that the service manager is looking for
usually only takes a second or two, and is not related to the size of the database, but none the less there is some risk of timeout.
:::

## Docker

You can run EventStoreDB in a Docker container as a single node, using insecure mode. It is useful in most
cases to try out the product and for local development purposes.

It's also possible to run a three-node cluster with or without SSL using Docker Compose. Such a setup is
closer to what you'd run in production.

### Run with Docker

EventStoreDB Docker images are now hosted in the registry `docker.eventstore.com/eventstore`.

Pull the container with:

```bash
docker pull docker.eventstore.com/eventstore/eventstoredb-ee:latest
```

The following command will start the EventStoreDB node using the default HTTP port, without security. You can then connect to it using one of the clients and the `esdb://localhost:2113?tls=false` connection string. You can also access the Admin UI by opening http://localhost:2113 in your browser.

```bash
docker run --name esdb-node -it -p 2113:2113 \
    docker.eventstore.com/eventstore/eventstoredb-ee --insecure --run-projections=All
    --enable-atom-pub-over-http
```

Then, you'd be able to connect to EventStoreDB with gRPC clients. Also, the Stream Browser will work
in the Admin UI.

In order to sustainably keep the data, we also recommend mapping the database and index volumes.

### Use Docker Compose

You can also run a single-node instance or a three-node secure cluster locally using Docker Compose.

#### Insecure single node

You can use Docker Compose to run EventStoreDB in the same setup as the `docker run` command mentioned before.

Create a file `docker-compose.yaml` with the following content:

@[code{curl}](@samples/server/docker-compose.yaml)

Run the instance:

```bash
docker compose up
```

The command above would run EventStoreDB as a single node without SSL. You also get AtomPub protocol enabled, so you can get the stream browser to work in the Admin UI.

::: warning
The legacy TCP client protocol is disabled by default and is no longer be available from version 24.10. 
:::

#### Secure cluster

With Docker Compose, you can also run a three-node cluster with security enabled. This kind of setup is
something you'd expect to use in production.

Create file `docker-compose.yaml` with following content:

@[code{curl}](@samples/server/docker-compose-cluster.yaml)

Quite a few settings are shared between the nodes and we use the `env` file to avoid repeating those settings.
So, add the `vars.env` file to the same location:

@[code{curl}](@samples/server/vars.env)

Containers will use the shared volume using the local `./certs` directory for certificates. However, if you
let Docker create the directory on startup, the container won't be able to get write access to it.
Therefore, you should create the `certs` directory manually. You only need to do it once.

```bash
mkdir certs
```

Now you are ready to start the cluster.

```bash
docker compose up
```

Watching the log messages, you will see that after some time, the elections process completes. Then you're able to connect to each
node using the Admin UI. Nodes should be accessible on the loopback address (`127.0.0.1` or `localhost`) over
HTTP, using ports specified below:

| Node  | HTTP port |
|:------|:----------|
| node1 | 2111      |
| node2 | 2112      |
| node3 | 2113      |

You have to tell your client to use secure connection.

| Protocol | Connection string                                                                  |
|:---------|:-----------------------------------------------------------------------------------|
| gRPC     | `esdb://localhost:2111,localhost:2112,localhost:2113?tls=true&tlsVerifyCert=false` |

As you might've noticed, the connection string has a setting to disable the certificate validation (`tlsVerifyCert=false`). It would prevent the invalid certificate error since the cluster uses a private, auto-generated CA.

However, **we do not recommend using this setting in production**. Instead, you can either add the CA certificate to the trusted root CA store or instruct your application to use such a certificate. See the [security section](../security/protocol-security.md#certificate-installation-on-a-client-environment) for detailed instructions.

## Kubernetes

KurrentDB can be deployed and managed using the [Operator](/server/kubernetes-operator/).

## Building from source

You can also build [EventStoreDB from source](https://github.com/EventStore/EventStore?tab=readme-ov-file#building-eventstoredb). Before doing that, you need to install the .NET 8 SDK. EventStoreDB packages have the .NET Runtime embedded, so you don't need to install anything except the EventStoreDB package.

## Compatibility notes

Depending on how your EventStoreDB instance is configured, some features might not work. Below are some features that are unavailable due to the specified options.

| Feature                       | Options impact                                                                                                                                                                          |
|:------------------------------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Connection without SSL or TLS | EventStoreDB is secure by default. Your clients need to establish a secure connection, unless you use the `Insecure` option.                                                      |
| Authentication and ACLs       | When using the `Insecure` option for the server, all security is disabled. The `Users` menu item is also disabled in the Admin UI.                                                      |
| Projections                   | Running projections is disabled by default and the `Projections` menu item is disabled in the Admin UI. You need to enable projections explicitly by using the `RunProjections` option. |
| AtomPub protocol              | The AtomPub protocol is disabled by default. If you use this protocol, you have to explicitly enable it by using the `EnableAtomPubOverHttp` option.                          |
| Stream browser                | The stream browser feature in Admin UI depends on the AtomPub protocol and is greyed out by default. You need to enable AtomPub (previous line) to make the stream browser work.        |
