---
title: "Installation"
order: 3
---

<CloudBanner />

## Quick start

KurrentDB can run as a single node or as a highly-available cluster. For the cluster deployment, you'd need three server nodes.

The installation procedure consists of the following steps:

- Create a configuration file for each cluster node. If you are using any licensed features, ensure that you configure a [license key](#license-keys).
- Install KurrentDB on each node using one of the available methods.
- Obtain SSL certificates, either signed by a publicly trusted or private certificate authority.
- Copy the configuration files and SSL certificates to each node.
- Start the KurrentDB service on each node.
- Check the cluster status using the Admin UI on any node.

### Default access

| User  | Password |
|-------|----------|
| admin | changeit |
| ops   | changeit |

### License Keys

Some features of KurrentDB require a license key to access. When you purchase an enterprise subscription, the license key will be sent to your company's designated license administrator. Existing customers who would like to upgrade to a 24.10+ enterprise license should contact their Kurrent (formerly Event Store) account manager or contact us [here](https://www.kurrent.io/talk_to_expert). As an existing customer, you can also try the enterprise features by signing up for a [free trial license key](https://www.kurrent.io/kurrent_free_trial).

There are various ways to provide the license key to KurrentDB. For more information, refer to the [configuration guide](../configuration/README.md).

Configuration file:

```yaml
Licensing:
  LicenseKey: Yourkey
```

Environment variable:

```
KURRENTDB_LICENSING__LICENSE_KEY
```

For most features that require a license, KurrentDB will not start if the feature is enabled but the license key is not provided or is invalid.

## Package repositories

Packages for KurrentDB are hosted on [Cloudsmith](https://cloudsmith.io/~eventstore), in the following repositories:

<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) packages.-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) packages.
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview) containing non-production preview packages.

## Linux

KurrentDB has pre-built packages available on Cloudsmith for RedHat or Debian-based distributions.
The name of the KurrentDB package is `kurrentdb`.

### Ubuntu/Debian packages

Debian packages can be found in the following repositories:

<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts/packages/?q=format%3Adeb+name%3Akurrentdb) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) packages.-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest/packages/?q=format%3Adeb+name%3Akurrentdb) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) packages.
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview/packages/?q=format%3Adeb+name%3Akurrentdb) containing non-production preview packages.

#### Distribution setup
To install packages, you can quickly set up the repository automatically (recommended):

::: tabs
@tab kurrent-latest
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-latest/setup.deb.sh' \
  | sudo -E bash
```
<!--@tab kurrent-lts
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-lts/setup.deb.sh' \
  | sudo -E bash
```-->
@tab kurrent-preview
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-preview/setup.deb.sh' \
  | sudo -E bash
```
:::

If you need to force a specific distribution, release/version, architecture, or component (if supported), you can also do that (e.g. if your system is compatible but not identical):

::: tabs
@tab kurrent-latest
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-latest/setup.deb.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH component=COMPONENT bash
```
<!--@tab kurrent-lts
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-lts/setup.deb.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH component=COMPONENT bash
```-->
@tab kurrent-preview
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-preview/setup.deb.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH component=COMPONENT bash
```
:::

Alternatively, you can find instructions to manually configure it yourself on Cloudsmith:
<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts/setup/#formats-deb)-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest/setup/#formats-deb)
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview/setup/#formats-deb)

#### Install with apt-get

Install the package:

```bash
apt-get install kurrentdb=25.1.0
```

#### Uninstall with apt-get

You can uninstall the package with:

```bash
apt-get remove kurrentdb
```

If you want to also remove any configuration files and user settings, use:

```bash
apt-get purge kurrentdb
```

### RedHat packages

RedHat packages can be found in the following repositories:

<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts/packages/?q=format%3Arpm+name%3Akurrentdb) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) packages.-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest/packages/?q=format%3Arpm+name%3Akurrentdb) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) packages.
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview/packages/?q=format%3Arpm+name%3Akurrentdb) containing non-production preview packages.

#### Distribution setup

To install packages, you can quickly set up the repository automatically (recommended):

::: tabs
@tab kurrent-latest
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-latest/setup.rpm.sh' \
  | sudo -E bash
```
<!--@tab kurrent-lts
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-lts/setup.rpm.sh' \
  | sudo -E bash
```-->
@tab kurrent-preview
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-preview/setup.rpm.sh' \
  | sudo -E bash
```
:::

If you need to force a specific distribution, release/version, or architecture, you can also do that (e.g. if your system is compatible but not identical):

::: tabs
@tab kurrent-latest
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-latest/setup.rpm.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH bash
```
<!--@tab kurrent-lts
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-lts/setup.rpm.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH bash
```-->
@tab kurrent-preview
```bash
curl -1sLf \
  'https://packages.kurrent.io/public/kurrent-preview/setup.rpm.sh' \
  | sudo -E distro=DISTRO codename=CODENAME arch=ARCH bash
```
:::

Alternatively, you can find instructions to manually configure it yourself on Cloudsmith:
<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts/setup/#formats-rpm).-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest/setup/#formats-rpm).
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview/setup/#formats-rpm).

#### Install with yum

Install the package:

```bash
yum install kurrentdb-25.1.0-1.x86_64
```

#### Uninstall with yum

You can uninstall the package with:

```bash
yum remove kurrentdb
```

### Running the kurrentdb service

Once installed, the server is registered as a service. Therefore, you can start KurrentDB with:

```bash
systemctl start kurrentdb
```

When you install the KurrentDB package, the service doesn't start by default. This allows you to change the configuration located at `/etc/kurrentdb/kurrentdb.conf` and to prevent creating database and index files in the default location.

::: warning
We recommend that when using Linux you set the 'open file limit' to a high number. The precise value depends on your use case, but at least between `30,000` and `60,000`.
:::

## Windows

### NuGet

KurrentDB has NuGet packages available on [Chocolatey](https://community.chocolatey.org/packages/kurrentdb).

#### Install with Chocolatey

You can install KurrentDB through Chocolatey:

```powershell
choco install kurrentdb --version=25.1.0
```

KurrentDB can then be run with `KurrentDB.exe`:

```powershell
KurrentDB.exe --config {your config file}
```

#### Uninstall with Chocolatey

You can uninstall KurrentDB through Chocolatey with:

```powershell
choco uninstall kurrentdb
```

#### Running as a service

KurrentDB can be run as a windows service. The following commands are for example. Consult the [sc.exe documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config) for details. 

Create the service:

```powershell
sc.exe create "KurrentDB" `
  start= delayed-auto     `
  binpath= "C:\ProgramData\chocolatey\lib\kurrentdb\kurrentdb-25.1.0-windows.x64\KurrentDB.exe --config=c:\path\to\kurrentdb-config.yaml"
```

Configure the restart policy:

```powershell
sc.exe failure "KurrentDB" `
  reset= 0                 `
  actions= restart/5000/restart/5000/restart/5000
```

Start the service:

```powershell
sc.exe start "KurrentDB"
```

Logs will still be written to the usual log file location. By default on windows this is the `logs` directory within the directory that contains `KurrentDB.exe`.

::: warning
In this version the the service restart policy does not guarantee that the service is running. If running as a Windows Service, we recommend configuring
the Windows Task Scheduler to start the service regularly (say, every 10 seconds), which will do nothing if the service is already running.

This is for two reasons, both of which we intend to address in a future version:

1. If the service shuts down gracefully, such as when going offline for truncation, it will be not be restarted by the policy.
2. If the service does not start within 30s (by default), it will not be restarted by the policy. The 'start' that the service manager is looking for
usually only takes a second or two, and is not related to the size of the database, but none the less there is some risk of timeout.

Important: it is not recommended to run KurrentDB directly using Task Scheduler. By default Task Scheduler runs processes with low priority which will significantly impact performance.
:::

## Docker

You can run KurrentDB in a Docker container as a single node, using insecure mode. It is useful in most
cases to try out the product and for local development purposes.

It's also possible to run a three-node cluster with or without SSL using Docker Compose. Such a setup is
closer to what you'd run in production.

KurrentDB Docker images are hosted in the following registries:

<!--* [kurrent-lts](https://cloudsmith.io/~eventstore/repos/kurrent-lts/packages/?q=format%3Adocker+name%3Akurrentdb) containing only production-ready [LTS](../release-schedule/#long-term-support-releases) containers.-->
* [kurrent-latest](https://cloudsmith.io/~eventstore/repos/kurrent-latest/packages/?q=format%3Adocker+name%3Akurrentdb) containing production-ready LTS and [STS](../release-schedule/#short-term-support-releases) containers.
* [kurrent-preview](https://cloudsmith.io/~eventstore/repos/kurrent-preview/packages/?q=format%3Adocker+name%3Akurrentdb) containing non-production preview containers.

### Run with Docker

Pull the container with:

::: tabs
@tab kurrent-latest
```bash
docker pull docker.kurrent.io/kurrent-latest/kurrentdb:latest
```
<!--@tab kurrent-lts
```bash
docker pull docker.kurrent.io/kurrent-lts/kurrentdb:latest
```-->
:::

The following command will start the KurrentDB node using the default HTTP port, without security. You can then connect to it using one of the clients and the `kurrentdb://localhost:2113?tls=false` connection string. You can also access the Admin UI by opening http://localhost:2113 in your browser.

::: tabs
@tab kurrent-latest
```bash
docker run --name kurrentdb-node -it -p 2113:2113 \
    docker.kurrent.io/kurrent-latest/kurrentdb:latest --insecure --run-projections=All \
    --enable-atom-pub-over-http
```
<!--@tab kurrent-lts
```bash
docker run --name kurrentdb-node -it -p 2113:2113 \
    docker.kurrent.io/kurrent-lts/kurrentdb:latest --insecure --run-projections=All \
    --enable-atom-pub-over-http
```-->
:::

Then, you'd be able to connect to KurrentDB with gRPC clients. Also, the Stream Browser will work
in the Admin UI.

In order to sustainably keep the data, we also recommend mapping the database and index volumes.

### Use Docker Compose

You can also run a single-node instance or a three-node secure cluster locally using Docker Compose.

#### Insecure single node

You can use Docker Compose to run KurrentDB in the same setup as the `docker run` command mentioned before.

Create a file `docker-compose.yaml` with the following content:

@[code{curl}](@samples/server/docker-compose.yaml)

Run the instance:

```bash
docker compose up
```

The command above would run KurrentDB as a single node without SSL. You also get AtomPub protocol enabled, so you can get the stream browser to work in the Admin UI.

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
| gRPC     | `kurrentdb://localhost:2111,localhost:2112,localhost:2113?tls=true&tlsVerifyCert=false` |

As you might've noticed, the connection string has a setting to disable the certificate validation (`tlsVerifyCert=false`). It would prevent the invalid certificate error since the cluster uses a private, auto-generated CA.

However, **we do not recommend using this setting in production**. Instead, you can either add the CA certificate to the trusted root CA store or instruct your application to use such a certificate. See the [security section](../security/protocol-security.md#certificate-installation-on-a-client-environment) for detailed instructions.

## Kubernetes

KurrentDB can be deployed and managed using the [Operator](/server/kubernetes-operator/).

## Building from source

You can also build [KurrentDB from source](https://github.com/EventStore/EventStore?tab=readme-ov-file#building-kurrentdb). Before doing that, you need to install the .NET 8 SDK. KurrentDB packages have the .NET Runtime embedded, so you don't need to install anything except the KurrentDB package.

## Compatibility notes

Depending on how your KurrentDB instance is configured, some features might not work. Below are some features that are unavailable due to the specified options.

| Feature                       | Options impact                                                                                                                                                                          |
|:------------------------------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Connection without SSL or TLS | KurrentDB is secure by default. Your clients need to establish a secure connection, unless you use the `Insecure` option.                                                      |
| Authentication and ACLs       | When using the `Insecure` option for the server, all security is disabled. The `Users` menu item is also disabled in the Admin UI.                                                      |
| Projections                   | Running projections is disabled by default and the `Projections` menu item is disabled in the Admin UI. You need to enable projections explicitly by using the `RunProjections` option. |
| AtomPub protocol              | The AtomPub protocol is disabled by default. If you use this protocol, you have to explicitly enable it by using the `EnableAtomPubOverHttp` option.                          |
| Stream browser                | The stream browser feature in Admin UI depends on the AtomPub protocol and is greyed out by default. You need to enable AtomPub (previous line) to make the stream browser work.        |
