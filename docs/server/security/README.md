---
dir:
  text: Security
  order: 3
title: Security options
---

# Security

For production use, it is important to configure KurrentDB security features to prevent unauthorised access
to your data.

Security features of KurrentDB include:

- [Encryption in-flight](./protocol-security.md) using HTTPS and TLS
- [User authentication](./user-authentication.md) for allowing users with different roles to access the database
- [User authorization](./user-authorization.md) to restrict access to specific event streams
- [Encryption-at-rest](#encryption-at-rest) to protect data on disk
- [FIPS compatibility](#fips-140-2) to allow running KurrentDB on FIPS-enabled operating systems

## Configuration options

Below you can find more details about each of the available security options.

### Running without security

KurrentDB is secure by default. It means that you have to supply valid certificates and configuration for the database node to work.

We realise that many users want to try out the latest version with their existing applications without any security in their internal networks.

For this to work, you can use the `Insecure` option:

| Format               | Syntax                |
|:---------------------|:----------------------|
| Command line         | `--insecure`          |
| YAML                 | `Insecure`            |
| Environment variable | `KURRENTDB_INSECURE`  |

**Default**: `false`

::: warning
When running with protocol security disabled, everything is sent unencrypted over the wire. To make things explicit, KurrentDB **does not use any authentication and authorisation** (including ACLs) when running insecure.
:::

### Set initial passwords

You can set the admin and ops passwords on the first run of the database. This will not impact the existing credentials, the user can log into their accounts with exising passwords. Additionally, this will only take effect if you are using [basic authentication](./user-authentication.md#basic-authentication).

Set the password for the `admin` user with the `DefaultAdminPassword` option:

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Environment variable | `KURRENTDB_DEFAULT_ADMIN_PASSWORD`  |

**Default**: `changeit`

And set the password for the `ops` user with the `DefaultOpsPassword` option:

| Format               | Syntax                              |
|:---------------------|:------------------------------------|
| Environment variable | `KURRENTDB_DEFAULT_OPS_PASSWORD`    |

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
| Environment variable | `KURRENTDB_ALLOW_ANONYMOUS_STREAM_ACCESS`  |

**Default**: `false`

### Anonymous access to endpoints

Similarly to streams above, anonymous access has historically been available to some http endpoints.

Anonymous access to `/gossip`, `/stats` and the `HTTP OPTIONS` method can now be configured with the following two options. By default `/gossip` is still accessible anonymously but the others are not. Some clients currently rely on anonymous access to `/gossip`. This will likely change in the future.

The `AllowAnonymousEndpointAccess` option controls anonymous access to these endpoints. Setting `OverrideAnonymousEndpointAccessForGossip` to `true` allows anonymous access to `/gossip` specifically, overriding the other option.

| Format               | Syntax                                       |
|:---------------------|:---------------------------------------------|
| Command line         | `--allow-anonymous-endpoint-access`          |
| YAML                 | `AllowAnonymousEndpointAccess`               |
| Environment variable | `KURRENTDB_ALLOW_ANONYMOUS_ENDPOINT_ACCESS`  |

**Default**: `false`

| Format               | Syntax                                                     |
|:---------------------|:-----------------------------------------------------------|
| Command line         | `--override-anonymous-endpoint-access-for-gossip`          |
| YAML                 | `OverrideAnonymousEndpointAccessForGossip`                 |
| Environment variable | `KURRENTDB_OVERRIDE_ANONYMOUS_ENDPOINT_ACCESS_FOR_GOSSIP`  |

**Default**: `true`

::: tip
Anonymous access is still always granted to `/ping`, `/info`, the static content of the UI, and http redirects.
:::

## FIPS 140-2 

KurrentDB runs on FIPS 140-2 enabled operating systems.

The Federal Information Processing Standards (FIPS) of the United States are a set of publicly announced standards that the National Institute of Standards and Technology (NIST) has developed for use in computer systems of non-military United States government agencies and contractors.

The 140 series of FIPS (FIPS 140) are U.S. government computer security standards that specify requirements for cryptography modules.

To run KurrentDB on FIPS 140-2 enabled operating systems, the following is required:

1. The node's certificates & keys must be FIPS 140-2 compatible
    - Our certificate generation tool, [es-gencert-cli](https://github.com/EventStore/es-gencert-cli/releases), generates FIPS 140-2 compatible certificates & keys as from version 1.2.0 (only the linux build).
    - If you want to manually generate your certificates or keys with openssl, you must use openssl 3 or later.
    - If you want to use PKCS #12 bundles (.p12/.pfx extension), you must use a FIPS 140-2 compatible encryption algorithm (e.g AES-256) & hash algorithm (e.g SHA-256) to encrypt the bundle's contents.

Note that KurrentDB will also likely run properly on FIPS 140-3 compliant operating systems with the above steps but this cannot reliably be confirmed at the moment as FIPS 140-3 certification is still ongoing for the operating systems themselves.

## Encryption-At-Rest

<Badge type="info" vertical="middle" text="License Required"/>

The Encryption-At-Rest feature allows users to encrypt their KurrentDB database. Currently, only chunk files are encrypted - the indexes are not. The primary objective is to protect against an attacker who obtains access to the physical disk. In contrast to volume or filesystem encryption, file level encryption provides some degree of protection for attacks against the live system or remote exploits as the plaintext data is not directly readable. Protecting against memory-dump based attacks is out of the scope of this feature.

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

### Encryption Algorithm

Data is encrypted using a symmetric-key encryption algorithm.


The feature is designed to support different encryption algorithms. Currently, the following encryption algorithms are implemented:
- `AesGcm` - Advanced Encryption Standard (AES) Galois Counter Mode (GCM)
  - The default key size is 256 bits, but 128-bit and 192-bit keys are also supported.

### Master Key

The `master` key is the topmost key in the key hierarchy.

The size of the master key is not defined but it needs to be long enough (e.g at least 128 bits) to provide reasonable security.

Using a key-derivation function (HKDF in our case), the master key is used to derive multiple `data` keys. Each `data` key is used to encrypt a single chunk file. If the `master` key is compromised, the whole database can be decrypted. However, if a `data` key is compromised, only one chunk file can be decrypted.

Usually, only one `master` key should be necessary. However, to cater for scenarios where a `master` key is compromised, the feature supports loading multiple `master` keys. The latest `master` key is actively used to encrypt/decrypt new chunks, while old `master` keys are used only to decrypt old chunks - thus the new data is secure.

### Master Key Source

The function of a master key source is to load master keys.

Master keys are loaded from the source only once at process startup. They are then kept in memory throughout the lifetime of the process.

The feature is designed to support different master key sources. Currently, the following master key sources are implemented:
- `File` - loads master keys from a directory
  - The master key can be generated using [es-cli](https://cloudsmith.io/~eventstore/repos/eventstore/packages/?q=es-cli) with the command: `es-cli encryption generate-master-key`
  - For proper security, the master key files must be located on a drive other than where the database files are.
  - This master key source is not recommended to be used in production as it provides minimal protection.

You can contact Kurrent if you want us to support a particular master key source.

### Configuration

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

Sample configuration:

```yaml
Transform: aes-gcm

EncryptionAtRest:
  Enabled: true
  MasterKey:
    File:
      KeyPath: /path/to/keys/
  Encryption:
    AesGcm:
      Enabled: true
      KeySize: 256 # optional. supported key sizes: 128, 192, 256 (default)
```

When using the `File` master key source, a master key must be generated using the es-genkey-cli tool and placed in the configured `KeyPath` directory. The master key having the highest ID will be chosen as the active one.

Once the feature is enabled, the server should log messages similar to the following:

```
...
[141828, 1,11:42:45.325,INF] Encryption-At-Rest: Loaded master key source: "File"
[141828, 1,11:42:45.340,INF] Encryption-At-Rest: (File) Loaded master key: 2 (256 bits)
[141828, 1,11:42:45.340,INF] Encryption-At-Rest: (File) Loaded master key: 1 (256 bits)
[141828, 1,11:42:45.345,INF] Encryption-At-Rest: Active master key ID: 2
[141828, 1,11:42:45.345,INF] Encryption-At-Rest: Loaded encryption algorithm: "AesGcm"
[141828, 1,11:42:45.347,INF] Encryption-At-Rest: (AesGcm) Using key size: 256 bits
[141828, 1,11:42:45.401,INF] Loaded the following transforms: Identity, Encryption_AesGcm
[141828, 1,11:42:45.402,INF] Active transform set to: Encryption_AesGcm
...
```

New and scavenged chunks will be encrypted.

### Compatibility

When encryption is enabled, it's no longer possible to revert back to an unencrypted database if:
- at least one new chunk file has been created or
- at least one chunk has been scavenged

The encrypted chunks would first need to be decrypted before a user can revert to the `identity` transform. Special tooling or endpoints are not currently available to do this.

### Tutorial

[Learn how to set up and use Encryption-At-Rest in KurrentDB through a tutorial.](https://developers.kurrent.io/tutorials/Encryption-At-Rest.md)
