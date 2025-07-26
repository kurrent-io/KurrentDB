---
title: "Security"
---

## Security

For production use it is important to configure EventStoreDB security features to prevent unauthorised access
to your data.

Security features of EventStoreDB include:

- [User management](#authentication) for allowing users with different roles to access the database
- [Access Control Lists](#access-control-lists) to restrict access to specific event streams
- Encryption in-flight using HTTPS and TLS

### Protocol security

EventStoreDB supports gRPC and the proprietary TCP protocol for high-throughput real-time communication. It
also has some HTTP endpoints for the management operations like scavenging, creating projections and so on.
EventStoreDB also uses HTTP for the gossip seed endpoint, both internally for the cluster gossip, and
internally for clients that connect to the cluster using discovery mode.

All those protocols support encryption with TLS and SSL. Each protocol has its own security configuration, but
you can only use one set of certificates for both TLS and HTTPS.

The protocol security configuration depends a lot on the deployment topology and platform. We have created an
interactive [configuration tool](installation.md), which also has instructions on how to generate and install
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
When running with protocol security disabled, everything is sent unencrypted over the wire. In the previous version it included the server credentials. Sending username and password over the wire without encryption is not secure by definition, but it might give a false sense of security. In order to make things explicit, EventStoreDB v20 **does not use any authentication and authorisation** (including ACLs) when running insecure.
:::

### Certificates configuration

In this section, you can find settings related to protocol security (HTTPS and TLS).

#### Certificate common name

SSL certificates can be created with a common name (CN), which is an arbitrary string. Usually is contains the DNS name for which the certificate is issued. When cluster nodes connect to each other, they need to ensure that they indeed talk to another node and not something that pretends to be a node. Therefore, EventStoreDB expects the connecting party to have a certificate with a pre-defined CN `eventstoredb-node`.

When using the Event Store [certificate generator](#certificate-generation-tool), the CN is properly set by default. However, you might want to change the CN and in this case, you'd also need to tell EventStoreDB what value it should expect instead of the default one, using the setting below:

| Format               | Syntax                                             |
|:---------------------|:---------------------------------------------------|
| Command line         | `--certificate-reserved-node-common-name`          |
| YAML                 | `CertificateReservedNodeCommonName`                |
| Environment variable | `EVENTSTORE_CERTIFICATE_RESERVED_NODE_COMMON_NAME` |

**Default**: `eventstoredb-node`


::: warning
Server certificates **must** have the internal and external IP addresses or DNS names as subject alternative names.
:::

#### Trusted root certificates

When getting an incoming connection, the server needs to ensure if the certificate used for the connection can be trusted. For this to work, the server needs to know where trusted root certificates are located.

EventStoreDB will not use the default trusted root certificates store location of the platform. So, even if you use a certificate signed by a publicly trusted CA, you'd need to explicitly tell the node to use the OS default root certificate store. For certificates signed by a private CA, you just provide the path to the CA certificate file (but not the filename).

If you are running on Windows, you can also load the trusted root certificate from the Windows Certificate Store. The available options for configuring this are described [below](#certificate-store-windows).

| Format               | Syntax                                      |
|:---------------------|:--------------------------------------------|
| Command line         | `--trusted-root-certificates-paths`         |
| YAML                 | `TrustedRootCertificatesPath`               |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH` |

**Default**: n/a

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

If the certificate file doesn't contain the certificate private key, you need to tell the node where to find the key file using the `CertificatePrivateKeyFile` setting.

| Format               | Syntax                                    |
|:---------------------|:------------------------------------------|
| Command line         | `--certificate-private-key-file`          |
| YAML                 | `CertificatePrivateKeyFile`               |
| Environment variable | `EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE` |

::: warning RSA private key
EventStoreDB expects the private key to be in RSA format. Check the first line of the key file and ensure that it looks like this:
```
-----BEGIN RSA PRIVATE KEY-----
```
If you have non-RSA private key, you can use `openssl` to convert it:
```bash
openssl rsa -in privkey.pem -out privkeyrsa.pem
```
:::

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
To match any certificate made by the es-gencert-cli, you can set the subject name to `eventstoredb-node`.

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
To match any root certificate made through the es-gencert-cli, you can set the Subject Name to `EventStoreDB CA`.

If multiple matching root certificates are found, then the root certificate with the latest expiry date will be selected.

| Format               | Syntax                                             |
|:---------------------|:---------------------------------------------------|
| Command line         | `--trusted-root-certificate-subject-name`          |
| YAML                 | `TrustedRootCertificateSubjectName`                |
| Environment variable | `EVENTSTORE_TRUSTED_ROOT_CERTIFICATE_SUBJECT_NAME` |

### Certificate generation tool

Event Store provides the interactive Certificate Generation CLI, which creates certificates signed by a private, auto-generated CA for EventStoreDB. You can use the [configuration wizard](installation.md), that will provide you exact CLI commands that you need to run to generate certificates matching your configuration.

#### Getting started

CLI is available as Open Source project in the [Github Repository](https://github.com/EventStore/es-gencert-cli). The latest release can be found under the [GitHub releases page.](https://github.com/EventStore/es-gencert-cli/releases)

We're releasing binaries for Windows, Linux and macOS. We also publish the tool as a Docker image.

Basic usage for Certificate Generation CLI:

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

See more in the [complete sample of docker-compose secured cluster configuration.](installation.md#use-docker-compose)

### Certificate installation on a client environment

To connect to EventStoreDB, you need to install the auto-generated CA certificate file on the client machine (e.g. machine where the client is hosted, or your dev environment).

#### Linux (Ubuntu, Debian)

1. Copy auto-generated CA file to dir `/usr/local/share/ca-certificates/`, e.g. using command:
  ```bash
  sudo cp ca.crt /usr/local/share/ca-certificates/event_store_ca.crt
  ```
2. Update the CA store:
  ```bash
  sudo update-ca-certificates
  ```

#### Windows

1. You can manually import it to the local CA cert store through `Certificates Local Machine Management Console`. To do that select **Run** from the **Start** menu, and then enter `certmgr.msc`. Then import certificate to `Trusted Root Certification`.
2. You can also run the PowerShell script instead:

```powershell
Import-Certificate -FilePath ".\certs\ca\ca.crt" -CertStoreLocation Cert:\CurrentUser\Root
```

#### MacOS

1. In the Keychain Access app on your Mac, select either the login or System keychain. Drag the certificate file onto the Keychain Access app. If you're asked to provide a name and password, type the name and password for an administrator user on this computer.
2. You can also run the bash script:

```bash
sudo security add-certificates -k /Library/Keychains/System.keychain ca.crt 
```

### Intermediate CA certificates

Intermediate CA certificates are supported by loading them from a [PEM](https://datatracker.ietf.org/doc/html/rfc1422) or [PKCS #12](https://datatracker.ietf.org/doc/html/rfc7292) bundle specified by the [`CertificateFile` configuration parameter](#certificate-file). To make sure that the configuration is correct, the certificate chain is validated on startup with the node's own certificate.

If you've used the [certificate generation tool](#certificate-generation-tool) with the default settings to generate your CA and node certificates, then you're not using intermediate CA certificates.

However, if you're using a public certificate authority (e.g [Let's Encrypt](https://letsencrypt.org/)) to generate your node certificates there is a chance that you're using intermediate CA certificates without knowing. This is due to the [Authority Information Access (AIA)](https://datatracker.ietf.org/doc/html/rfc4325#section-2) extension which allows intermediate certificates to be fetched from a remote server.

To verify if your certificate is using the AIA extension, you need to verify if there is a section named: `Authority Information Access` in the certificate.

::: tabs#os
@tab Linux
Use `openssl` to find the section in the certificate file:

```
openssl x509 -in /path/to/node.crt -text | grep 'Authority Information Access' -A 1
```
@tab Windows
Open the certificate, go to the `Details` tab and look for the `Authority Information Access` field.

If the extension is present, you can manually download the intermediate certificate from the URL present under the `CA Issuers` entry.
Note that you will usually need to convert the downloaded certificate from the `DER` to the `PEM` format.

This can be done with the following `openssl` command:

```bash
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

| Format               | Syntax                                |
|:---------------------|:--------------------------------------|
| Command line         | `--disable-external-tcp-tls`          |
| YAML                 | `DisableExternalTcpTls`               |
| Environment variable | `EVENTSTORE_DISABLE_EXTERNAL_TCP_TLS` |

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

