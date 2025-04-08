# User Certificates Plugin
The User Certificates plugin allows users to provide an x509 user certificate for authentication in addition to username and password.

A user certificate is valid on any cluster where the user exists and where the node certificates for the cluster share a common root CA with the user certificate. This means that you can have a single user certificate that is valid across multiple clusters.

To enable user certificate authentication you will need to download the plugin and add it to the `.\plugins` folder in your EventStoreDB installation directory.

You then need to add the configuration to enable the plugin to a json file in the `.\config` directory. For example, add the following configuration to `.\config\plugin-config.json` within the EventStoreDB installation directory:

```
{
  "EventStore": {
    "UserCertificates": {
      "Enabled": true
    }
  }
}
```

Once the plugin is installed and enabled the server should log a similar message to the one below:

```
[13260, 8,13:59:10.445,INF] UserCertificatesPlugin: user X.509 certificate authentication is enabled
```

You can then connect to the server with a user certificate. For example, in curl:

```
curl -i https://localhost:2113/streams/%24all --cert user-admin.crt --key user-admin.key
```

## Creating a user certificate

The user certificate has to meet the following requirements:
- The certificate has a root CA in common with the node certificate
- The root CA that they have in common is trusted by the node
- The CN of the certificate is the same as the username
- The certificate has the ClientAuth EKU, and not the ServerAuth EKU
- The certificate must be in date
- The CN is the username of a user that exists in the database

> Note: User certificates may also be used for the `admin` and `ops` users.

You can generate a user certificate with the [es-gencert-cli](https://github.com/EventStore/es-gencert-cli/pull/23) (currently in a branch):

For example, to generate a user certificate with the username `ouro`:

```
./es-gencert-cli.exe create-user -username ouro -ca-certificate ca.crt -ca-key ca.key -days 10
```

The resulting certificate looks like this:

<details>
    <summary>Example Certificate</summary>

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

</details>


## Restrictions

There is currently a limitation with this plugin that requests authenticated with user certificates cannot be forwarded between nodes.

This affects Writes, Persistent Subscription operations, and Projections operations. These requests will need to be performed on the Leader node only.

The User Certificates plugin cannot be used alongside any of the other authentication plugins, such as the LDAPS plugin or the OAuth plugin.

## Troubleshooting

### Plugin not loaded
The plugin has to be located in a subdirectory of the server's `plugins` directory.
To check this:
1. Go to the installation directory of the server, the directory containing the EventStoreDb executable.
1. In this directory there should be a directoy called `plugins`, create it if this is not the case.
1. The `plugins` directory should have a subdirectoy for the plugin, for instance called `EventStore.Auth.UserCertificates` but this could be any name. Create it if it doesn't exist.
1. The binaries of the plugin should be located in the subdirectory which was checked in the previous step.

You can verify which plugins have been found and loaded by looking for the following logs:

```
[12824, 1,17:30:04.921,INF] Plugins path: "C:\eventstoredb-24.2.0-rc.1-ce-windows.x64\plugins"
[12824, 1,17:30:04.922,INF] Adding: "C:\eventstoredb-24.2.0-rc.1-ce-windows.x64\plugins" to the plugin catalog.
[12824, 1,17:30:04.924,INF] Adding: "C:\eventstoredb-24.2.0-rc.1-ce-windows.x64\plugins\EventStore.Auth.UserCertificates" to the plugin catalog.
[12824, 1,17:30:05.253,INF] Loaded SubsystemsPlugin plugin: "user-certificates" "24.2.0.1".
```

### Plugin not started
The plugin has to be configured to start in order to authenticate user requests.

If you see the following log it means the plugin was found but was not started:

```
[28056, 1,11:44:52.441,INF] UserCertificatesPlugin is not enabled
```

Ensure the plugin is enabled in the EventStoreDB configuration.

When the plugin starts correctly you should see the following log:

```
[13260, 8,13:59:10.445,INF] UserCertificatesPlugin: user X.509 certificate authentication is enabled
```

### The plugin is enabled but the user is not authenticated
If the plugin has been enabled but you are still getting access denied errors, check the following:

1. The user exists and is enabled in the EventStoreDB database. Can you log in with the username and password?
1. The user certificate is valid, and has a valid chain up to a trusted root CA.
1. The user certificate and node certificate share a common root CA.
1. Use 'requires leader' (which is the default) in your client configuration to rule out issues with forwarding requests.
