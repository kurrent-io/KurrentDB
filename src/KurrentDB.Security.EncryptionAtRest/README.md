# Encryption-At-Rest Plugin
The Encryption-At-Rest plugin allows users to encrypt their EventStoreDB database. Currently, only chunk files are encrypted - the indexes are not. The primary objective is to protect against an attacker who obtains access to the physical disk. In contrast to volume level encryption, file level encryption provides some degree of protection for attacks against the live system or remote exploits as the plaintext data is not directly readable. Protecting against memory-dump based attacks is out of the scope of this plugin.

## Encryption Algorithm
Data is encrypted using a symmetric-key encryption algorithm.


The plugin is designed to support different encryption algorithms. Currently, the following encryption algorithms are implemented:
- `AesGcm` - Advanced Encryption Standard (AES) Galois Counter Mode (GCM)
  - The default key size is 256 bits, but 128-bit and 192-bit keys are also supported.

## Master Key
The `master` key is the topmost key in the key hierarchy.

The size of the master key is not defined but it needs to be long enough (e.g at least 128 bits) to provide reasonable security.

Using a key-derivation function (HKDF in our case), the master key is used to derive multiple `data` keys. Each `data` key is used to encrypt a single chunk file. If the `master` key is compromised, the whole database can be decrypted. However, if a `data` key is compromised, only one chunk file can be decrypted.

Usually, only one `master` key should be necessary. However, to cater for scenarios where a `master` key is compromised, the plugin supports loading multiple `master` keys. The latest `master` key is actively used to encrypt/decrypt new chunks, while old `master` keys are used only to decrypt old chunks - thus the new data is secure.

## Master Key Source
The function of a master key source is to load master keys.

Master keys are loaded from the source only once at process startup. They are then kept in memory throughout the lifetime of the process.

The plugin is designed to support different master key sources. Currently, the following master key sources are implemented:
- `File` - loads master keys from a directory
  - The master key can be generated using [es-genkey-cli](https://github.com/EventStore/es-genkey-cli)
  - For proper security, the master key files must be located on a drive other than where the database files are.
  - This master key source is not recommended to be used in production as it provides minimal protection.

You can contact Event Store if you want us to support a particular master key source.

## Configuration
- By default, the encryption plugin is bundled with EventStoreDB and located inside the `plugins` directory.

- A JSON configuration file (e.g. `encryption-config.json`)  needs to be added in the `./config` directory located within the EventStoreDB installation directory:

```
{
  "EventStore": {
    "EncryptionAtRest": {
      "Enabled": true,
      "MasterKey": {
        "File": {
          "KeyPath": "/path/to/keys/"
        }
      },
      "Encryption": {
        "AesGcm": {
          "Enabled": true,
          "KeySize": 256 # optional. supported key sizes: 128, 192, 256 (default)
        }
      }
    }
  }
}
```

- The `Transform` configuration parameter must be specified in the server's configuration file:

```
Transform: aes-gcm
```

- When using the `File` master key source, a master key must be generated using [es-genkey-cli](https://github.com/EventStore/es-genkey-cli) and placed in the configured `KeyPath` directory. The master key having the highest ID will be chosen as the active one.

Once the plugin is installed and enabled the server should log messages similar to the following:

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

## Compatibility
When encryption is enabled, it's no longer possible to revert back to an unencrypted database if:
- at least one new chunk file has been created or
- at least one chunk has been scavenged

The encrypted chunks would first need to be decrypted before a user can revert to the `identity` transform. Special tooling or endpoints are not currently available to do this.
