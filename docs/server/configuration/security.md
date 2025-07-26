---
order: 2
---

# Security

For production use, it is important to configure EventStoreDB security features to prevent unauthorised access
to your data.

Security features of EventStoreDB include:

- [User management](#authentication) for allowing users with different roles to access the database
- [Access Control Lists](#access-control-lists) to restrict access to specific event streams
- Encryption in-flight using HTTPS and TLS

## Protocol security

EventStoreDB supports gRPC and the proprietary TCP protocol for high-throughput real-time communication. It
also has some HTTP endpoints for the management operations like scavenging, creating projections and so on.
EventStoreDB also uses HTTP for the gossip seed endpoint, both internally for the cluster gossip, and
internally for clients that connect to the cluster using discovery mode.

All those protocols support encryption with TLS and SSL. Each protocol has its own security configuration, but
you can only use one set of certificates for both TLS and HTTPS.

The protocol security configuration depends a lot on the deployment topology and platform. We have created an
interactive [configuration tool](https://configurator.eventstore.com), which also has instructions on how to generate and install
the certificates and configure EventStoreDB nodes to use them.

## Security options

Below you can find more details about each of the available security options.

### Running without security

Unlike previous versions, EventStoreDB v20+ is secure by default. It means that you have to supply valid certificates and configuration for the database node to work.

We realise that many users want to try out the latest version with their existing applications, and also run a previous version of EventStoreDB without any security in their internal networks.

For this to work, you can use the `Insecure` option:

| Format               | Syntax                |
|:---------------------|:----------------------|
| Command line         | `--insecure`          |
| YAML                 | `Insecure`            |
| Environment variable | `EVENTSTORE_INSECURE` |

**Default**: `false`

::: warning
When running with protocol security disabled, everything is sent unencrypted over the wire. In the previous version it included the server credentials. Sending username and password over the wire without encryption is not secure by definition, but it might give a false sense of security. To make things explicit, EventStoreDB v20+ **does not use any authentication and authorisation** (including ACLs) when running insecure.
:::

### Set initial passwords

We are adding an ability to set default admin and ops passwords on the first run of the database. It will not impact the existing credentials, the user can log into their accounts with exising passwords.

For this to work, you can use the `DefaultAdminPassword` option:

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Environment variable | `EVENTSTORE_DEFAULT_ADMIN_PASSWORD` |

**Default**: `changeit`

For this to work, you can use the `DefaultOpsPassword` option:

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Environment variable | `EVENTSTORE_DEFAULT_OPS_PASSWORD`   |

**Default**: `changeit`

::: warning
Due to security reasons the `DefaultAdminPassword` and `DefaultOpsPassword` options can only be set through environment variables. The user will receive the error message if they try to pass the options using command line or config file.
:::

### Anonymous access to streams

Historically, anonymous users with network access have been allowed to read/write streams that do not have access control lists.

This is now disabled by default but can be enabled by setting `AllowAnonymousStreamAccess` to `true`.

| Format               | Syntax                                     |
|:---------------------|:-------------------------------------------|
| Command line         | `--allow-anonymous-stream-access`          |
| YAML                 | `AllowAnonymousStreamAccess`               |
| Environment variable | `EVENTSTORE_ALLOW_ANONYMOUS_STREAM_ACCESS` |

**Default**: `false`

### Anonymous access to endpoints

Similarly to streams above, anonymous access has historically been available to some http endpoints.

Anonymous access to `/gossip`, `/stats` and the `HTTP OPTIONS` method can now be configured with the following two options. By default `/gossip` is still accessible anonymously but the others are not. Some clients currently rely on anonymous access to `/gossip`. This will likely change in the future.

The `AllowAnonymousEndpointAccess` option controls anonymous access to these endpoints. Setting `OverrideAnonymousEndpointAccessForGossip` to `true` allows anonymous access to `/gossip` specifically, overriding the other option.

| Format               | Syntax                                       |
|:---------------------|:---------------------------------------------|
| Command line         | `--allow-anonymous-endpoint-access`          |
| YAML                 | `AllowAnonymousEndpointAccess`               |
| Environment variable | `EVENTSTORE_ALLOW_ANONYMOUS_ENDPOINT_ACCESS` |

**Default**: `false`

| Format               | Syntax                                                     |
|:---------------------|:-----------------------------------------------------------|
| Command line         | `--override-anonymous-endpoint-access-for-gossip`          |
| YAML                 | `OverrideAnonymousEndpointAccessForGossip`                 |
| Environment variable | `EVENTSTORE_OVERRIDE_ANONYMOUS_ENDPOINT_ACCESS_FOR_GOSSIP` |

**Default**: `true`

::: tip
Anonymous access is still always granted to `/ping`, `/info`, the static content of the UI, and http redirects.
:::

### Certificates configuration

In this section, you can find settings related to protocol security (HTTPS and TLS).

#### Certificate common name

SSL certificates can be created with a common name (CN), which is an arbitrary string. Usually it contains the DNS name for which the certificate is issued.

When cluster nodes connect to each other, they need to ensure that they indeed talk to another node and not something that pretends to be a node. To achieve that, EventStoreDB authenticates a connecting node by ensuring that it supplies a trusted client certificate having a CN that matches exactly with the CN in its own certificate. This essentially means that the CN must be the same across all node certificates by default. For example, when using the Event Store [certificate generator](#certificate-generation-tool), the CN is set to `eventstoredb-node` in all node certificates.

::: note
Prior to version 23.10.0, the `CertificateReservedNodeCommonName` setting needed to be configured if a user had certificates with a CN other than `eventstoredb-node`. EventStoreDB will now, by default, automatically read the CN from the node's certificate and use it as the `CertificateReservedNodeCommonName`. However, if you still choose to specify the `CertificateReservedNodeCommonName` in your configuration, it will take precedence.
:::

In practice, it's not always possible to obtain certificates where the CN is the same across all nodes. For instance, when using a public CA, single-domain certificates are very common and these certificates cannot be used on multiple nodes as they are valid for exactly one host. In this case, the `CertificateReservedNodeCommonName` setting can be configured with a wildcard as per the following example:

If the domains are `node1.esdb.mycompany.org`, `node2.esdb.mycompany.org` and `node3.esdb.mycompany.org`, then `CertificateReservedNodeCommonName` must be set to `*.esdb.mycompany.org`.

| Format               | Syntax                                             |
|:---------------------|:---------------------------------------------------|
| Command line         | `--certificate-reserved-node-common-name`          |
| YAML                 | `CertificateReservedNodeCommonName`                |
| Environment variable | `EVENTSTORE_CERTIFICATE_RESERVED_NODE_COMMON_NAME` |

::: warning
Server certificates **must** have the internal and external IP addresses (`ReplicationIp` and `NodeIp` respectively) or DNS names as subject alternative names.
:::

#### Trusted root certificates

When getting an incoming connection, the server needs to ensure if the certificate used for the connection can be trusted. For this to work, the server needs to know where trusted root certificates are located.

EventStoreDB will use the default trusted root certificates location of `/etc/ssl/certs` when running on Linux only. So if you are running on Windows or a platform with a different default certificates location, you'd need to explicitly tell the node to use the OS default root certificate store. For certificates signed by a private CA, you just provide the path to the CA certificate file (but not the filename).

If you are running on Windows, you can also load the trusted root certificate from the Windows Certificate Store. The available options for configuring this are described [below](#certificate-store-windows).

| Format               | Syntax                                      |
|:---------------------|:--------------------------------------------|
| Command line         | `--trusted-root-certificates-paths`         |
| YAML                 | `TrustedRootCertificatesPath`               |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH` |

**Default**: n/a on Windows, `/etc/ssl/certs` on Linux

#### Certificate file

The `CertificateFile` setting needs to point to the certificate file, which will be used by the cluster node.

| Format               | Syntax                        |
|:---------------------|:------------------------------|
| Command line         | `--certificate-file`          |
| YAML                 | `CertificateFile`             |
| Environment variable | `EVENTSTORE_CERTIFICATE_FILE` |

If the certificate file is protected by password, you'd need to set the `CertificatePassword` value accordingly, so the server can load the certificate.

| Format               | Syntax                            |
|:---------------------|:----------------------------------|
| Command line         | `--certificate-password`          |
| YAML                 | `CertificatePassword`             |
| Environment variable | `EVENTSTORE_CERTIFICATE_PASSWORD` |

If the certificate file doesn't contain the certificate private key, you need to tell the node where to find the key file using the `CertificatePrivateKeyFile` setting. The private key can be in RSA, or PKCS8 format.

| Format               | Syntax                                    |
|:---------------------|:------------------------------------------|
| Command line         | `--certificate-private-key-file`          |
| YAML                 | `CertificatePrivateKeyFile`               |
| Environment variable | `EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE` |

If the private key file is an encrypted PKCS #8 file, then you need to provide the password with the `CertificatePrivateKeyPassword` option.

| Format               | Syntax                                        |
|:---------------------|:----------------------------------------------|
| Command line         | `--certificate-private-key-password`          |
| YAML                 | `CertificatePrivateKeyPassword`               |
| Environment variable | `EVENTSTORE_CERTIFICATE_PRIVATE_KEY_PASSWORD` |


#### Certificate store (Windows)

The certificate store location is the location of the Windows certificate store, for example `CurrentUser`.

| Format               | Syntax                                  |
|:---------------------|:----------------------------------------|
| Command line         | `--certificate-store-location`          |
| YAML                 | `CertificateStoreLocation`              |
| Environment variable | `EVENTSTORE_CERTIFICATE_STORE_LOCATION` |

The certificate store name is the name of the Windows certificate store, for example `My`.

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Command line         | `--certificate-store-name`          |
| YAML                 | `CertificateStoreName`              |
| Environment variable | `EVENTSTORE_CERTIFICATE_STORE_NAME` |

You can load a certificate using either its thumbprint or its subject name.
If using the thumbprint, the server expects to only find one certificate file matching that thumbprint in the cert store.

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Command line         | `--certificate-thumbprint`          |
| YAML                 | `CertificateThumbprint`             |
| Environment variable | `EVENTSTORE_CERTIFICATE_THUMBPRINT` |

The subject name matches any certificate that contains the specified name. This means that multiple matching certificates could be found.
To match any certificate made by the `es-gencert-cli` tool, you can set the subject name to `eventstoredb-node`.

If multiple matching certificates are found, then the certificate with the latest expiry date will be selected.

| Format               | Syntax                                |
|:---------------------|:--------------------------------------|
| Command line         | `--certificate-subject-name`          |
| YAML                 | `CertificateSubjectName`              |
| Environment variable | `EVENTSTORE_CERTIFICATE_SUBJECT_NAME` |

When you are loading your node certificates from the Windows cert store, you are likely to want to load the trusted root certificate from the cert store as well.
The options to configure this are similar to the ones for node certificates.

The trusted root certificate store location is the location of the Windows certificate store in which the trusted root certificate is installed, for example `CurrentUser`.

| Format               | Syntax                                               |
|:---------------------|:-----------------------------------------------------|
| Command line         | `--trusted-root-certificate-store-location`          |
| YAML                 | `TrustedRootCertificateStoreLocation`                |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATE_STORE_LOCATION` |

The trusted root certificate store name is the name of the Windows certificate store in which the trusted root certificate is installed, for example `Root`.

| Format               | Syntax                                           |
|:---------------------|:-------------------------------------------------|
| Command line         | `--trusted-root-certificate-store-name`          |
| YAML                 | `TrustedRootCertificateStoreName`                |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATE_STORE_NAME` |

Trusted root certificates can also be loaded using either its thumbprint or its subject name.
If using the thumbprint, the server expects to only find one trusted root certificate file matching that thumbprint in the cert store.

| Format               | Syntax                                           |
|:---------------------|:-------------------------------------------------|
| Command line         | `--trusted-root-certificate-thumbprint`          |
| YAML                 | `TrustedRootCertificateThumbprint`               |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATE_THUMBPRINT` |

The subject name matches any certificate that contains the specified name. This means that multiple matching certificates could be found.
To match any root certificate made through the `es-gencert-cli` tool, you can set the Subject Name to `EventStoreDB CA`.

If multiple matching root certificates are found, then the root certificate with the latest expiry date will be selected.

| Format               | Syntax                                             |
|:---------------------|:---------------------------------------------------|
| Command line         | `--trusted-root-certificate-subject-name`          |
| YAML                 | `TrustedRootCertificateSubjectName`                |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATE_SUBJECT_NAME` |

### Certificate generation tool

Event Store provides the interactive Certificate Generation CLI, which creates certificates signed by a private, auto-generated CA for EventStoreDB. You can use the [configuration wizard](https://configurator.eventstore.com), that will provide you exact CLI commands that you need to run to generate certificates matching your configuration.

#### Getting started

The CLI is available as Open Source project in the [GitHub Repository](https://github.com/EventStore/es-gencert-cli). The latest release can be found under the [GitHub releases page.](https://github.com/EventStore/es-gencert-cli/releases)

We're releasing binaries for Windows, Linux and macOS. We also publish the tool as a Docker image.

Basic usage for the Certificate Generation CLI:

```bash
./es-gencert-cli [options] <command> [args]
```

Getting help for a specific command:

```bash
./es-gencert-cli -help <command>
```

::: warning
If you are running EventStoreDB on Linux, remember that all certificate files should have restrictive rights, otherwise the OS won't allow using them.
Usually, you'd need to change rights for each certificate file to prevent the "permissions are too open" error.

You can do it by running the following command:

```bash
chmod 600 [file]
```
:::

#### Generating the CA certificate

As the first step CA certificate needs to be generated. It'll need to be trusted for each of the nodes and client environment.

By default, the tool will create the `ca` directory in the `certs` directory you created. Two keys will be generated:
- `ca.crt` - public file that need to be used also for the nodes and client configuration,
- `ca.key` - private key file that should be used only in the node configuration. **Do not copy it to client environment**.

CA certificate will be generated with pre-defined CN `eventstoredb-node`.

To generate CA certificate run:

```bash
./es-gencert-cli create-ca
```

You can customise generated cert by providing following params:

| Param   | Description                                                       |
|:--------|:------------------------------------------------------------------|
| `-days` | The validity period of the certificate in days (default: 5 years) |
| `-out`  | The output directory (default: ./ca)                              |

Example:

```bash
./es-gencert-cli create-ca -out ./es-ca
```

#### Generating the Node certificate

You need to generate certificates signed by the CA for each node. They should be installed only on the specific node machine.

By default, the tool will create the `ca` directory in the `certs` directory you created. Two keys will be generated:
- `node.crt` - the public file that needs to be also used for the nodes and client configuration,
- `node.key` - the private key file that should be used only in the node's configuration. **Do not copy it to client environment**.

To generate node certificate run command:

```bash
./es-gencert-cli -help create_node
```

You can customise generated cert by providing following params:

| Param             | Description                                                                   |
|:------------------|:------------------------------------------------------------------------------|
| `-ca-certificate` | The path to the CA certificate file (default: `./ca/ca.crt`)                  |
| `-ca-key`         | The path to the CA key file (default: `./ca/ca.key`)                          |
| `-days`           | The output directory (default: `./nodeX` where X is an auto-generated number) |
| `-out`            | The output directory (default: `./ca`)                                        |
| `-ip-addresses`   | Comma-separated list of IP addresses of the node                              |
| `-dns-names`      | Comma-separated list of DNS names of the node                                 |

::: warning
While generating the certificate, you need to remember to pass internal end external:
- IP addresses to `-ip-addresses`: e.g. `127.0.0.1,172.20.240.1` and/or
- DNS names to `-dns-names`: e.g. `localhost,node1.eventstore`
  that will match the URLs that you will be accessing EventStoreDB nodes.
  :::

Sample:

```
./es-gencert-cli-cli create-node \
    -ca-certificate ./es-ca/ca.crt \
    -ca-key ./es-ca/ca.key \
    -out ./node1 \
    -ip-addresses 127.0.0.1,172.20.240.1 \
    -dns-names localhost,node1.eventstore
```

#### Running with Docker

You could also run the tool using Docker interactive container:

```bash
docker run --rm -i eventstore/es-gencert-cli <command> <options>
```

One useful scenario is to use the Docker Compose file tool to generate all the necessary certificates before starting cluster nodes.

Sample:

```yaml
version: "3.5"

services:
  setup:
    image: eventstore/es-gencert-cli:1.0.2
    entrypoint: bash
    user: "1000:1000"
    command: >
      -c "mkdir -p ./certs && cd /certs
      && es-gencert-cli create-ca
      && es-gencert-cli create-node -out ./node1 -ip-addresses 127.0.0.1,172.20.240.1 -dns-names localhost,node1.eventstore
      && es-gencert-cli create-node -out ./node1 -ip-addresses 127.0.0.1,172.20.240.2 -dns-names localhost,node2.eventstore
      && es-gencert-cli create-node -out ./node1 -ip-addresses 127.0.0.1,172.20.240.3 -dns-names localhost,node3.eventstore
      && find . -type f -print0 | xargs -0 chmod 666"
    container_name: setup
    volumes:
      - ./certs:/certs
```

See more in the [complete sample of docker-compose secured cluster configuration.](../quick-start/installation.md#use-docker-compose)

### Certificate installation on a client environment

To connect to EventStoreDB, you need to install the auto-generated CA certificate file on the client machine (e.g. machine where the client is hosted, or your dev environment).

::: tabs#os
@tab Linux

1. Copy auto-generated CA file to dir `/usr/local/share/ca-certificates/`, e.g. using command:
  ```bash
  sudo cp ca.crt /usr/local/share/ca-certificates/event_store_ca.crt
  ```
2. Update the CA store:
  ```bash
  sudo update-ca-certificates
  ```

@tab Windows

1. You can manually import it to the local CA cert store through `Certificates Local Machine Management Console`. To do that select **Run** from the **Start** menu, and then enter `certmgr.msc`. Then import certificate to `Trusted Root Certification`.
2. You can also run the PowerShell script instead:

```powershell
Import-Certificate -FilePath ".\certs\ca\ca.crt" -CertStoreLocation Cert:\CurrentUser\Root
```

@tab macOS

1. In the Keychain Access app on your Mac, select either the login or System keychain. Drag the certificate file onto the Keychain Access app. If you're asked to provide a name and password, type the name and password for an administrator user on this computer.
2. You can also run the bash script:

```bash
sudo security add-certificates -k /Library/Keychains/System.keychain ca.crt 
```
:::

### Intermediate CA certificates

Intermediate CA certificates are supported by loading them from a [PEM](https://datatracker.ietf.org/doc/html/rfc1422) or [PKCS #12](https://datatracker.ietf.org/doc/html/rfc7292) bundle specified by the [`CertificateFile` configuration parameter](#certificate-file). To make sure that the configuration is correct, the certificate chain is validated on startup with the node's own certificate.

If you've used the [certificate generation tool](#certificate-generation-tool) with the default settings to generate your CA and node certificates, then you're not using intermediate CA certificates.

However, if you're using a public certificate authority (e.g [Let's Encrypt](https://letsencrypt.org/)) to generate your node certificates there is a chance that you're using intermediate CA certificates without knowing. This is due to the [Authority Information Access (AIA)](https://datatracker.ietf.org/doc/html/rfc4325#section-2) extension which allows intermediate certificates to be fetched from a remote server.

To verify if your certificate is using the AIA extension, you need to verify if there is a section named: `Authority Information Access` in the certificate.

::: tabs#os
@tab Linux

Use `openssl` to find the section in the certificate file:

```bash
openssl x509 -in /path/to/node.crt -text | grep 'Authority Information Access' -A 1
```
@tab Windows
Open the certificate, go to the `Details` tab and look for the `Authority Information Access` field.

If the extension is present, you can manually download the intermediate certificate from the URL present under the `CA Issuers` entry.
Note that you will usually need to convert the downloaded certificate from the `DER` to the `PEM` format.

This can be done with the following `openssl` command:

```powershell
openssl x509 -inform der -in /path/to/cert.der > /path/to/cert.pem
```

or with [an online service](https://www.sslshopper.com/ssl-converter.html) if you don't have openssl installed.
:::

It's possible that there are more than one intermediate CA certificates in the chain - so you need to verify if the certificate you've just downloaded also uses the AIA extension. If yes, you need to download the next intermediate CA certificate in the chain by repeating the same process above until you eventually reach a publicly trusted root certificate (i.e. the `Subject` and `Issuer` fields will match). In practice, there'll usually be at most two intermediate certificates in the chain.

#### Bundling the intermediate certificates

The node's certificate should be first in the bundle, followed by the intermediates. Intermediates can be in any order but it would be good to keep it from leaf to root, as per the usual convention. The root certificate should not be bundled.

In the examples below, intermediate certificates are numbered from 1 to N starting from the leaf and going up.

##### PEM format

If your node's certificate and the intermediate CA certificates are both PEM formatted, that is they begin with `-----BEGIN CERTIFICATE-----` and end with `-----END CERTIFICATE-----` then you can simply append the contents of the intermediate certificate files to the end of the node's certificate file to create the bundle.

::: tabs#os
@tab Linux

```bash
cat /path/to/intermediate1.crt >> /path/to/node.crt
...
cat /path/to/intermediateN.crt >> /path/to/node.crt
```

@tab Windows

```powershell
type C:\path\to\intermediate1.crt >> C:\path\to\node.crt
...
type C:\path\to\intermediateN.crt >> C:\path\to\node.crt
```
:::

##### PKCS #12 format

If you want to generate a PKCS #12 bundle from PEM formatted certificate files, please follow the steps below.

```bash
cat /path/to/intermediate1.crt >> ./ca_bundle.crt
...
cat /path/to/intermediateN.crt >> ./ca_bundle.crt

openssl pkcs12 -export -in /path/to/node.crt -inkey /path/to/node.key -certfile ./ca_bundle.crt -out /path/to/node.p12 -passout pass:<password>
```

#### Adding intermediate certificates to the certificate store

Intermediate certificates also need to be added to the current user's certificate store.

This is required for two reasons:  
i)  For the full certificate chain to be sent when TLS connections are established  
ii) To improve performance by preventing certificate downloads if your certificate uses the AIA extension  

::: tabs#os
@tab Linux
The following script assumes EventStoreDB is running under the `eventstore` account.

```bash
sudo su eventstore --shell /bin/bash
dotnet tool install --global dotnet-certificate-tool
 ~/.dotnet/tools/certificate-tool add -s CertificateAuthority -l CurrentUser --file /path/to/intermediate.crt
```

@tab Windows
To import the intermediate certificate in the `Intermediate Certification Authorities` certificate store, run the following PowerShell command under the same account as EventStoreDB is running:

```powershell
Import-Certificate -FilePath .\path\to\intermediate.crt -CertStoreLocation Cert:\CurrentUser\CA
```

Optionally, to import the intermediate certificate in the `Local Computer` store, run the following as `Administrator`:

```powershell
Import-Certificate -FilePath .\ca.crt -CertStoreLocation Cert:\LocalMachine\CA
```
:::

### TCP protocol security

Although TCP is disabled by default for external connections (clients), cluster nodes still use TCP for replication. If you aren't running EventStoreDB in insecure mode, all TCP communication will use TLS using the same certificates as SSL.

You can, however, disable TLS for both internal and external TCP.

| Format               | Syntax                                |
|:---------------------|:--------------------------------------|
| Command line         | `--disable-internal-tcp-tls`          |
| YAML                 | `DisableInternalTcpTls`               |
| Environment variable | `EVENTSTORE_DISABLE_INTERNAL_TCP_TLS` |

**Default**: `false`

## Authentication

EventStoreDB supports authentication based on usernames and passwords out of the box. The Enterprise version
also supports LDAP as the authentication source.

Authentication is applied to all HTTP endpoints, except `/info`, `/ping`, `/stats`, `/elections` (only `GET`)
, `/gossip` (only `GET`) and static web content.

### Default users

EventStoreDB provides two default users, `$ops` and `$admin`.

`$admin` has full access to everything in EventStoreDB. It can read and write to protected streams, which is
any stream that starts with \$, such as `$projections-master`. Protected streams are usually system streams,
for example, `$projections-master` manages some projections' states. The `$admin` user can also run
operational commands, such as scavenges and shutdowns on EventStoreDB.

An `$ops` user can do everything that an `$admin` can do except manage users and read from system streams (
except for `$scavenges` and `$scavenges-streams`).

### New users

New users created in EventStoreDB are standard non-authenticated users. Non-authenticated users are
allowed `GET` access to the `/info`, `/ping`, `/stats`, `/elections`, and `/gossip` system streams.

`POST` access to the `/elections` and `/gossip` system streams is only allowed on the internal HTTP service.

By default, any user can read any non-protected stream unless there is an ACL preventing that.

### Externalised authentication

You can also use the trusted intermediary header for externalized authentication that allows you to integrate
almost any authentication system with EventStoreDB. Read more
about [the trusted intermediary header](#trusted-intermediary).

### Disable HTTP authentication

It is possible to disable authentication on all protected HTTP endpoints by setting
the `DisableFirstLevelHttpAuthorization` setting to `true`. The setting is set to `false` by default. When
enabled, the setting will force EventStoreDB to use the supplied credentials only to check the stream access
using [ACLs](#access-control-lists).

## Access control lists

By default, authenticated users have access to the whole EventStoreDB database. In addition to that, it allows
you to use Access Control Lists (ACLs) to set up more granular access control. In fact, the default access
level is also controlled by a special ACL, which is called the [default ACL](#default-acl).

### Stream ACL

EventStoreDB keeps the ACL of a stream in the stream metadata as JSON with the below definition:

```json
{
  "$acl": {
    "$w": "$admins",
    "$r": "$all",
    "$d": "$admins",
    "$mw": "$admins",
    "$mr": "$admins"
  }
}
```

These fields represent the following:

- `$w` The permission to write to this stream.
- `$r` The permission to read from this stream.
- `$d` The permission to delete this stream.
- `$mw` The permission to write the metadata associated with this stream.
- `$mr` The permission to read the metadata associated with this stream.

You can update these fields with either a single string or an array of strings representing users or
groups (`$admins`, `$all`, or custom groups). It's possible to put an empty array into one of these fields,
and this has the effect of removing all users from that permission.

::: tip 
We recommend you don't give people access to `$mw` as then they can then change the ACL.
:::

### Default ACL

The `$settings` stream has a special ACL used as the default ACL. This stream controls the default ACL for
streams without an ACL and also controls who can create streams in the system, the default state of these is
shown below:

```json
{
  "$userStreamAcl": {
    "$r": "$all",
    "$w": "$all",
    "$d": "$all",
    "$mr": "$all",
    "$mw": "$all"
  },
  "$systemStreamAcl": {
    "$r": "$admins",
    "$w": "$admins",
    "$d": "$admins",
    "$mr": "$admins",
    "$mw": "$admins"
  }
}
```

You can rewrite these to the `$settings` stream with the following request:

@[code{curl}](@samples/server/default-settings.sh)

The `$userStreamAcl` controls the default ACL for user streams, while all system streams use
the `$systemStreamAcl` as the default.

::: tip 
The `$w` in `$userStreamAcl` also applies to the ability to create a stream. Members of `$admins`
always have access to everything, you cannot remove this permission.
:::

When you set a permission on a stream, it overrides the default values. However, it's not necessary to specify
all permissions on a stream. It's only necessary to specify those which differ from the default.

Here is an example of the default ACL that has been changed:

```json
{
  "$userStreamAcl": {
    "$r": "$all",
    "$w": "ouro",
    "$d": "ouro",
    "$mr": "ouro",
    "$mw": "ouro"
  },
  "$systemStreamAcl": {
    "$r": "$admins",
    "$w": "$admins",
    "$d": "$admins",
    "$mr": "$admins",
    "$mw": "$admins"
  }
}
```

This default ACL gives `ouro` and `$admins` create and write permissions on all streams, while everyone else
can read from them. Be careful allowing default access to system streams to non-admins as they would also have
access to `$settings` unless you specifically override it.

Refer to the documentation of the HTTP API or SDK of your choice for more information about changing ACLs
programmatically.

## Trusted intermediary

The trusted intermediary header helps EventStoreDB to support a common security architecture. There are
thousands of possible methods for handling authentication and it is impossible for us to support them all. The
header allows you to configure a trusted intermediary to handle the authentication instead of EventStoreDB.

A sample configuration is to enable OAuth2 with the following steps:

- Configure EventStoreDB to run on the local loopback.
- Configure nginx to handle OAuth2 authentication.
- After authenticating the user, nginx rewrites the request and forwards it to the loopback to EventStoreDB
  that serves the request.

The header has the form of `{user}; group, group1` and the EventStoreDB ACLs use the information to handle
security.

```http:no-line-numbers
ES-TrustedAuth: "root; admin, other"
```

Use the following option to enable this feature:

| Format               | Syntax                           |
|:---------------------|:---------------------------------|
| Command line         | `--enable-trusted-auth`          |
| YAML                 | `EnableTrustedAuth`              |
| Environment variable | `EVENTSTORE_ENABLE_TRUSTED_AUTH` |

## FIPS 140-2 

<Badge type="info" vertical="middle" text="Commercial"/>

EventStoreDB runs on FIPS 140-2 enabled operating systems, this feature requires a commercial licence.

The Federal Information Processing Standards (FIPS) of the United States are a set of publicly announced standards that the National Institute of Standards and Technology (NIST) has developed for use in computer systems of non-military United States government agencies and contractors.

The 140 series of FIPS (FIPS 140) are U.S. government computer security standards that specify requirements for cryptography modules.

To run EventStoreDB on FIPS 140-2 enabled operating systems, the following is required:

1. The MD5 plugin must be installed. It is available on our [commercial downloads page](https://developers.eventstore.org/). To install it, unzip it in the `plugins` directory inside the server installation directory and restart the server.
2. The node's certificates & keys must be FIPS 140-2 compatible
    - Our certificate generation tool, [es-gencert-cli](https://github.com/EventStore/es-gencert-cli/releases), generates FIPS 140-2 compatible certificates & keys as from version 1.2.0 (only the linux build).
    - If you want to manually generate your certificates or keys with openssl, you must use openssl 3 or later.
    - If you want to use PKCS #12 bundles (.p12/.pfx extension), you must use a FIPS 140-2 compatible encryption algorithm (e.g AES-256) & hash algorithm (e.g SHA-256) to encrypt the bundle's contents.

Note that EventStoreDB will also likely run properly on FIPS 140-3 compliant operating systems with the above steps but this cannot reliably be confirmed at the moment as FIPS 140-3 certification is still ongoing for the operating systems themselves.

## LDAP authentication 

<Badge type="info" vertical="middle" text="Commercial"/>

The LDAP Authentication plugin enables EventStoreDB to use LDAP-based directory services for authentication. 

### Configuration steps

To set up EventStoreDB with LDAP authentication, follow these steps on the [database node's configuration file](../configuration/README.md). Remember to stop the service before making changes, and then start it. 

1. Change the authentication type to `ldaps`.
2. Add a section named `LdapsAuth` for LDAP-specific settings. 

Example configuration file in YAML format:

```yaml
AuthenticationType: ldaps
LdapsAuth:
  Host: 13.88.9.49
  Port: 636 #to use plaintext protocol, set Port to 389 and UseSSL to false 
  UseSSL: true
  ValidateServerCertificate: false #set this to true to validate the certificate chain
  AnonymousBind: false
  BindUser: cn=binduser,dc=mycompany,dc=local
  BindPassword: p@ssw0rd!
  BaseDn: ou=Lab,dc=mycompany,dc=local
  ObjectClass: organizationalPerson
  Filter: sAMAccountName
  RequireGroupMembership: false #set this to true to allow authentication only if the user is a member of the group specified by RequiredGroupDn
  GroupMembershipAttribute: memberOf
  RequiredGroupDn: cn=ES-Users,dc=mycompany,dc=local
  PrincipalCacheDurationSec: 60
  LdapGroupRoles:
      'cn=ES-Accounting,ou=Staff,dc=mycompany,dc=local': accounting
      'cn=ES-Operations,ou=Staff,dc=mycompany,dc=local': it
      'cn=ES-Admins,ou=Staff,dc=mycompany,dc=local': '$admins'
```

Upon successful LDAP authentication, users are assigned roles based on their domain group memberships, as specified in the `LdapGroupRoles` section. EventStoreDB supports two built-in roles: `$admins` and `$ops`, which can be assigned to users. 

### Troubleshooting 

If you encounter issues, check the server's log. Common problems include: 

| Error                                                                                       | Solution                                                                                                                                                                                                                                                                                                                                                                                                                                        |
|:--------------------------------------------------------------------------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Invalid Bind Credentials Specified                                                          | Confirm the `BindUser` and `BindPassword`.                                                                                                                                                                                                                                                                                                                                                                                                      |
| Exception During Search - 'No such Object' or 'The object does not exist'                   | Verify the `BaseDn`.                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 'Server Certificate Error' or 'Connect Error - The authentication or decryption has failed' | Verify that the server certificate is valid. If it is a self-signed certificate, set `ValidateServerCertificate` to `false`.                                                                                                                                                                                                                                                                                                                    |
| LDAP Server Unavailable                                                                     | Verify connectivity to the LDAP server from an EventStoreDB node (e.g. using `netcat` or `telnet`). Verify the `Host` and `Port` parameters.                                                                                                                                                                                                                                                                                                    |
| Error Authenticating with LDAP server                                                       | Verify the `ObjectClass` and `Filter` parameters. If you have set `RequireGroupMembership` to `true`, verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.                                                                                                                                                                                       |
| Error Authenticating with LDAP server. System.AggregateException                            | This packaging error may occur when setting `UseSSL: true` on Windows. Extract `Mono.Security.dll` to the _EventStore_ folder (where _EventStore.ClusterNode.exe_ is located) as a workaround.                                                                                                                                                                                                                                                  |
| No Errors in Server Logs But Cannot Login                                                   | <ul><li>Verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.</li><li>Verify the `ObjectClass` and `Filter` parameters.</li><li>If you have set `RequireGroupMembership` to `true`, verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.</li></ul> |


## User X.509 Certificates 

<Badge type="info" vertical="middle" text="Commercial"/>

The User Certificates plugin allows authentication through an X.509 user certificate in addition to username and password. User certificates work across any cluster that shares a trusted root Certificate Authority (CA) with the user's certificate. This means that you can have a single user certificate that is valid across multiple clusters.

### Configuration steps

Refer to the general [plugins configuration](../configuration/plugins.md) guide to see how to configure plugins with JSON files and environment variables.

Sample json configuration:
```json
{
  "EventStore": {
    "Plugins": {
      "UserCertificates": {
        "Enabled": true
      }
    }
  }
}
```

If the plugin is enabled, the server will log the following on startup:

```
UserCertificatesPlugin: user X.509 certificate authentication is enabled
```

### Connecting with a user certificate

Use the following command as an example to connect using `curl`: 

```bash
curl -i https://localhost:2113/streams/%24all --cert user-admin.crt --key user-admin.key
```

For using X.509 user certificate with EventStoreDB client from an application, refer to the client's documentation.

### Creating a user certificate

The user certificate must adhere to the following requirements:

- The certificate has a root CA in common with the node certificate.
- The root CA that they have in common is trusted by the node.
- The certificate has the ClientAuth EKU, and not the ServerAuth EKU.
- The certificate must be in date.
- The CN is the username of a user that exists in the database.

::: tip
Certificates can also authenticate `admin` and `ops` users.
:::

You can generate a user certificate with the [es-gencert-cli](https://github.com/EventStore/es-gencert-cli/):

For example, to generate a user certificate with the username `ouro`:

```bash
./es-gencert-cli.exe create-user -username ouro -ca-certificate ca.crt -ca-key ca.key -days 10
```

::: details Click to view a sample user certificate

```
X509 Certificate:
Version: 3
Serial Number: 6339e77cacd508002e45cf38418e4f69
Signature Algorithm:
    Algorithm ObjectId: 1.2.840.113549.1.1.11 sha256RSA
    Algorithm Parameters:
    05 00
Issuer:
    CN=EventStoreDB CA f2993c31201bd4bb5a59f3e580d32865
    O=Event Store Ltd
    C=UK
  Name Hash(sha1): cd4323cbb5259cfee1e1e01aaf472552cf18cbf2
  Name Hash(md5): 993adcf28aa235c593611234e6abc1fc

 NotBefore: 2024-02-26 5:24 PM
 NotAfter: 2024-03-07 5:24 PM

Subject:
    CN=ouro
  Name Hash(sha1): fe1463b6b0edf7f66dd22101c7d1c38f14d1292e
  Name Hash(md5): f40719c54125d3a27290bb48d6f73b15

Public Key Algorithm:
    Algorithm ObjectId: 1.2.840.113549.1.1.1 RSA
    Algorithm Parameters:
    05 00
Public Key Length: 2048 bits
Public Key: UnusedBits = 0
    0000  30 82 01 0a 02 82 01 01  00 9d 7b f2 25 72 0c b6
    0010  65 01 02 16 37 60 b9 22  14 04 bf fc 54 b2 e9 6f
    0020  c7 bc 43 8d c0 ca 3d 55  86 b4 3e 24 88 fc 70 7e
    0030  f6 f4 38 d3 52 2d 57 a0  48 8e 91 7b f4 10 8b fd
    0040  95 6b 41 99 d7 9b e4 d9  42 c8 c5 47 5e d1 0c 4c
    0050  3a 5a ff a3 db ea 0d 11  89 0c 3d 7f fb 9c e5 6c
    0060  4a 04 e7 f7 48 ff 0f 2f  63 fa be bf f5 f3 76 a1
    0070  ea 02 27 4c ca 27 82 64  40 b5 ef f6 6f fe f2 02
    0080  8b 16 ea 45 54 fd ba 80  2c 72 02 d0 14 41 d5 cd
    0090  db ae 6e 4d 9b 67 60 63  ae 92 ab 2d f4 8b 70 70
    00a0  d0 4f b1 97 66 71 9b c9  2a 5e 7a e3 d0 f5 0b eb
    00b0  9e 37 81 09 75 fa 81 d6  3c 9f 6a e6 56 8b 1f 73
    00c0  d0 61 4c f5 51 16 44 da  dd 3b b1 2b b9 f2 44 94
    00d0  53 a2 08 5a 8d 4a 4e cb  d7 cb f7 0c ca 7a 1a fb
    00e0  21 db 00 3e 75 85 c6 d3  95 bd 39 61 d1 ff 89 e7
    00f0  88 ab b0 58 ff ce e8 d7  19 a9 75 62 dc 98 a8 8f
    0100  58 31 a7 7c 1d 88 ba 51  5d 02 03 01 00 01
Certificate Extensions: 5
    2.5.29.15: Flags = 1(Critical), Length = 4
    Key Usage
        Digital Signature, Key Encipherment (a0)

    2.5.29.37: Flags = 0, Length = c
    Enhanced Key Usage
        Client Authentication (1.3.6.1.5.5.7.3.2)

    2.5.29.19: Flags = 1(Critical), Length = 2
    Basic Constraints
        Subject Type=End Entity
        Path Length Constraint=None

    2.5.29.14: Flags = 0, Length = 22
    Subject Key Identifier
        01fc7f1e364f1767b759dae23d43bbb1cbf2d75deabe605737969489b8de201d

    2.5.29.35: Flags = 0, Length = 24
    Authority Key Identifier
        KeyID=5e13f92550df9af08cdab327161ef2d2ef764b77fc4246bc2b172e1d9ed81b2f

Signature Algorithm:
    Algorithm ObjectId: 1.2.840.113549.1.1.11 sha256RSA
    Algorithm Parameters:
    05 00
Signature: UnusedBits=0
    0000  8f a4 00 65 f1 a5 53 54  a8 ed de 98 cc 51 f9 1c
    0010  49 7d f0 7c d6 9f 14 fa  13 b4 db b3 8b 56 59 6a
    0020  d1 67 a7 e3 21 d5 2d e3  c9 73 9b 09 a9 25 74 35
    0030  79 16 a2 ce 62 ff fd f1  5e 05 0c b6 29 3d c8 ab
    0040  1f 7f 87 66 d3 a1 a1 23  7e 11 03 1f 28 eb af d2
    0050  ac ef 7b 2c 0f b2 18 b4  59 b1 5a ba c9 35 5a b6
    0060  58 ef 55 4f e7 8c 43 35  1f 22 5c 6d 65 ff 6d 7c
    0070  af 32 3c b5 ba 00 c4 74  91 73 32 54 22 4c 52 f5
    0080  de cf f4 ed 71 37 f6 4a  5f 3b ba 37 2c 29 77 9a
    0090  ad a0 6f c9 a0 26 b3 a6  d3 0c 99 d6 1c f2 f0 b9
    00a0  29 51 c7 cc 8e 47 2c 04  0f b4 6f aa 09 5b c6 a4
    00b0  e3 b9 0b 07 3c 49 e6 62  b7 78 3b ff 81 d1 8c 26
    00c0  3e af 24 ae 01 54 91 33  96 a1 aa 14 05 b4 0a 5b
    00d0  42 3c 11 f1 f1 2c 93 9d  6d 8b b8 ec eb c6 e5 e7
    00e0  a4 9a d4 59 fb ff b7 ba  e1 33 c2 f8 09 52 80 c6
    00f0  95 95 51 35 08 1a 65 41  76 bc c2 62 2b 71 b3 65
Non-root Certificate
Key Id Hash(rfc-sha1): 8a3c17e6662711175142740cfeb0f52d2bdd3596
Key Id Hash(sha1): 87a30feaaeabd616f4dad92a9f1b44a43e2d0b52
Subject Key Id (precomputed): 01fc7f1e364f1767b759dae23d43bbb1cbf2d75deabe605737969489b8de201d
Key Id Hash(bcrypt-sha1): b808b4e710e4f6f158a837155838a295cfbb17bf
Key Id Hash(bcrypt-sha256): eaf799bb5f3d57dbb3afc0f5357a8723574c8c254bf159fce1c95d76f6085ae5
Key Id Hash(md5): fbb024d546e2039af7f3e01c71278313
Key Id Hash(sha256): ca10f7cb5af9bf9dff8f2ad43c3cbdb0647da1173631018cb8551812c425d575
Key Id Hash(pin-sha256): MZu7Waanp4kbJ/Hx3/tpgMb6L3GnXB3PrMY+9MDBmSg=
Key Id Hash(pin-sha256-hex): 319bbb59a6a7a7891b27f1f1dffb6980c6fa2f71a75c1dcfacc63ef4c0c19928
Cert Hash(md5): 1719850bd6f5f09c3610e4c33d13f7c2
Cert Hash(sha1): 12c5293973bda1df2c74ab280d492135171c8ab6
Cert Hash(sha256): a37767096f10697fb0fcf94a3872d0984aeed4754a295c5320679876e00def32
Signature Hash: 6d922badaba2372070f13c69b620286262eab1d8d2d2156a271a1d73aaaf64e4
```

:::

### Limitations

1. Requests authenticated with user certificates cannot be forwarded between nodes. This affects Writes, Persistent Subscription operations, and Projections operations. These requests will need to be performed on the Leader node only.

2. The X.509 authentication cannot be used in conjunction with any of the other authentication plugins, such as the LDAP plugin.

### Troubleshooting

| Error                                     | Solution                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
|:------------------------------------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Plugin not loaded                         | The plugin is only available in commercial editions. Check that it is present in `<installation-directory/plugins`.<br/><br/> If it is present, on startup the server will log a message similar to: `Loaded SubsystemsPlugin plugin: "user-certificates"`.                                                                                                                                                                                                                                                                |
| Plugin not enabled                        | The plugin has to be enabled in order to authenticate user requests.<br/><br/> The following log indicates that the plugin was found but not enabled: `UserCertificatesPlugin is not enabled`.                                                                                                                                                                                                                                                                                                                             |
| Plugin enabled and user not authenticated | If the plugin has been enabled but there are still access denied errors, check the following: <ul><li>The user exists and is enabled in the EventStoreDB database. Can you log in with the username and password?</li><li>The user certificate is valid, and has a valid chain up to a trusted root CA.</li><li>The user certificate and node certificate share a common root CA.</li><li>Use 'requires leader' (which is the default) in your client configuration to rule out issues with forwarding requests.</li></ul> |
