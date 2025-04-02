---
title: User authentication
order: 2
---

# Authentication

EventStoreDB provides the following means to authenticate users connecting to the database:

- [Basic authentication](#basic-authentication) is enabled by default, and allows you to authenticate users with a username and password. Users are created and managed in EventStoreDB.
- [User X.509 certificates](#user-x509-certificates) to authenticate users with an X.509 certificate. This is in addition to basic authentication.
- [LDAP authentication](#ldap-authentication) to authenticate users against an LDAP server such as OpenLDAP or Acive Directory.
- [OAuth authentication](#oauth-authentication) to authenticate users against an identity provider such as Auth0 or Identity Server

Once a user has been authenticated, they must be [authorized](./user-authorization.md) before they can perform actions on the database.

## Basic authentication

EventStoreDB supports authentication based on usernames and passwords out of the box.

These users are created and managed within EventStoreDB. So if you back up and restore a database with configured users, the same users will exist on the restored node.

### Default users

EventStoreDB provides two default users, `$ops` and `$admin`.

`$admin` has full access to everything in EventStoreDB. It can read and write to protected streams, which is any stream that starts with `$`, such as `$projections-$all` or `$users`. Protected streams are usually system streams, for example, `$projections-$all` manages which projections exist in EventStoreDB.

The `$admin` user can also run operational commands, such as scavenges and shutdowns on EventStoreDB.

An `$ops` user can do everything that an `$admin` can do except manage users and read from system streams.

### New users

New users can be created through the following means:

- The [HTTP API](@httpapi/security.md#creating-users)
- The [Admin UI](../features/admin-ui.md#users)
<!-- TODO A [gRPC client]() -->

Users are created with a username, password, and optional set of groups or roles. These roles are used for [authorization](./user-authorization.md#roles).

::: tip
Users managed in EventStoreDB are always granted a role that matches their username.
:::

### Externalised authentication

You can also use the trusted intermediary header for externalized authentication that allows you to integrate
almost any authentication system with EventStoreDB.

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

### Disable HTTP authentication

It is possible to disable authentication on all protected HTTP endpoints by setting
the `DisableFirstLevelHttpAuthorization` setting to `true`. The setting is set to `false` by default. When
`true`, the setting will force EventStoreDB to use the supplied credentials only to check [stream access](./user-authorization.md#stream-access).

## User X.509 Certificates 

<Badge type="info" vertical="middle" text="License Required"/>

The User Certificates feature allows authentication through an X.509 user certificate in addition to username and password. User certificates work across any cluster that shares a trusted root Certificate Authority (CA) with the user's certificate. This means that you can have a single user certificate that is valid across multiple clusters.

### Configuration steps

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

Sample configuration:

```yaml
UserCertificates:
  Enabled: true
```

If the feature is enabled, the server will log the following on startup:

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
| Feature not enabled                        | The feature has to be enabled in order to authenticate user requests.<br/><br/> The following log indicates that the feature was not enabled: `UserCertificatesPlugin is not enabled`.                                                                                                                                                                                                                                                                                                                             |
| Feature enabled and user not authenticated | If the feature has been enabled but there are still access denied errors, check the following: <ul><li>The user exists and is enabled in the EventStoreDB database. Can you log in with the username and password?</li><li>The user certificate is valid, and has a valid chain up to a trusted root CA.</li><li>The user certificate and node certificate share a common root CA.</li><li>Use 'requires leader' (which is the default) in your client configuration to rule out issues with forwarding requests.</li></ul> |

## LDAP authentication 

<Badge type="info" vertical="middle" text="License Required"/>

The LDAP Authentication feature enables EventStoreDB to use LDAP-based directory services for authentication. 

### Configuration

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

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

Upon successful LDAP authentication, users are assigned [roles](./user-authorization.md#roles) based on their domain group memberships, as specified in the `LdapGroupRoles` section.

### Troubleshooting 

If you encounter issues, check the server's log. Common problems include: 

| Error                                                                                       | Solution                                                                                                                                                                                                                                                                                                                                                                                                                                        |
|:--------------------------------------------------------------------------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Invalid Bind Credentials Specified                                                          | Confirm the `BindUser` and `BindPassword`.                                                                                                                                                                                                                                                                                                                                                                                                      |
| Exception During Search - 'No such Object' or 'The object does not exist'                   | Verify the `BaseDn`.                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 'Server Certificate Error' or 'Connect Error - The authentication or decryption has failed' | Verify that the server certificate is valid. If it is a self-signed certificate, set `ValidateServerCertificate` to `false`.                                                                                                                                                                                                                                                                                                                    |
| LDAP Server Unavailable                                                                     | Verify connectivity to the LDAP server from an EventStoreDB node (e.g. using `netcat` or `telnet`). Verify the `Host` and `Port` parameters.                                                                                                                                                                                                                                                                                                    |
| Error Authenticating with LDAP server                                                       | Verify the `ObjectClass` and `Filter` parameters. If you have set `RequireGroupMembership` to `true`, verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.                                                                                                                                                                                       |
| No Errors in Server Logs But Cannot Login                                                   | <ul><li>Verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.</li><li>Verify the `ObjectClass` and `Filter` parameters.</li><li>If you have set `RequireGroupMembership` to `true`, verify that the user is part of the group specified by `RequiredGroupDn` and that the LDAP record has `GroupMembershipAttribute` set to `memberOf`.</li></ul> |

## OAuth Authentication

<Badge type="info" vertical="middle" text="License Required"/>

The OAuth feature allows EventStoreDB to connect to an identity server and authenticate users based on a JWT rather than username and password.

Access tokens can contain a "role" claim, which will be used for [authorization](./user-authorization.md).

::: tip
With the default basic authentication, EventStoreDB treats the username as a "role" in addition to the groups they are in. If you want to have the same behaviour with OAuth authentication, you will need to add the user's username as an additional role claim.
:::

### Configuration

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

You can enable the OAuth plugin by setting the following options in you eventstore config file:
- Set the `AuthenticationType` to `oauth`
- Provide oauth configuration in a section named `OAuth`

For example:

:::tabs
@tab eventstore.conf
```yaml
AuthenticationType: oauth
OAuth:
  Audience: {audience}
  Issuer: {identity_server_endpoint}
  ClientId: {client_id}
  ClientSecret: {client_secret}
```
:::

Alternatively you can provide a path to a separate config file with the `AuthenticationConfig` option. For example:

:::tabs
@tab eventstore.conf
```yaml
AuthenticationType: oauth
AuthenticationConfig: ./es-oauth.conf
```

@tab es-oauth.conf
```yaml
OAuth:
  Audience: {audience}
  Issuer: {identity_server_endpoint}
  ClientId: {client_id}
  ClientSecret: {client_secret}
```
:::

The configuration options are:

| Name 			              | Type	  | Required?	| Default 	|Description |
|-------------------------|---------|-----------|-----------|------------|
| Audience 			          | string 	| Y 		| - 		| The audience used in your identity provider. |
| Issuer 			            | string 	| Y 		| - 		| The issuer endpoint for your identity provider. |
| ClientId			          | string 	| N 		| "" 		| The id of the client configured in the identity provider. |
| ClientSecret 		        | string 	| N 	  | "" 		| The client secret configured in the identity provider. |
| DisableIssuerValidation | bool 	  | N 		| false 	| Disable issuer validation for testing purposes. |
| Insecure 			          | bool 	  | N 		| false 	| Whether to validate the certificates for the identity provider. This is not related to `Insecure` in the EventStoreDB configuration. |

### Testing with a local identity server

You can try out the OAuth feature locally with this [Identity Server 4 docker container](https://github.com/EventStore/idsrv4). This container is not intended to be used in production.

You will first configure and run the identity server, and then configure and run EventStoreDB.

1. Create a `users.conf.json` file to configure the users for your test.

::: details users.conf.json
```json
[
  {
    "subjectId": "1",
    "username": "admin",
    "password": "password",
    "claims": [{
        "type": "role",
        "value": "$admins"
      }, {
        "type": "role",
        "value": "$ops"
      }]
  }, {
    "subjectId": "2",
    "username": "operator",
    "password": "password",
    "claims": [{
      "type": "role",
      "value": "$ops"
    }]
  }, {
    "subjectId": "3",
    "username": "ouro",
    "password": "password",
    "claims": [{
      "type": "role",
      "value": "custom"
    }]
  }, {
    "subjectId": "4",
    "username": "user",
    "password": "password",
    "claims": []
  }
]
```
:::

This creates the following users:

| Username 		  | Password 		| Roles |
|---------------|-------------|-------|
| `admin`		    | `password` 	| `$admins`, `$ops` |
| `operator` 	  | `password` 	| `$ops` |
| `ouro`		    | `password`	| `custom` |
| `user`		    | `password`	| None |

2. Create an identity server config file, `idsrv4.conf.json`.

You need to configure the following:

- A role claim (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) must be added to the token.
- The grant types of `password` and `authorization_code` must be allowed.
- A redirect uri of `{eventstore_server_ip}/oauth/callback` must be allowed for the legacy UI to function.

::: details idsrv4.conf.json
```json
{
	"IdentityResources": [
		{
			"Name": "openid",
			"DisplayName": "Your user identifier",
			"Required": true,
			"UserClaims": [
				"sub",
				"role"
			]
		},
		{
			"Name": "profile",
			"DisplayName": "User profile",
			"Description": "Your user profile information (first name, last name, etc.)",
			"Emphasize": true,
			"UserClaims": [
				"name",
				"given_name",
				"middle_name",
			]
		}
	],
	"ApiResources": [
		{
			"Name": "eventstore-client",
			"Scopes": [
				"streams",
				"openid",
				"profile"
			]
		}
	],
	"ApiScopes": [
		{
			"Name": "streams",
			"UserClaims": [
				"http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
				"role"
			]
		}
	],
	"Clients": [
		{
			"ClientId": "eventstore-client",
			"AllowedGrantTypes": [
				"password",
				"authorization_code",
                "client_credentials"
			],
			"ClientSecrets": [
				{
					"Value": "{client_secret}"
				}
			],
			"AllowedScopes": [
				"streams",
				"openid",
				"profile",
			],
			"RedirectUris": ["https://localhost:2113/oauth/callback","https://127.0.0.1:2113/oauth/callback"],
			"AlwaysIncludeUserClaimsInIdToken": true,
			"RequireConsent": false,
			"AlwaysSendClientClaims": true,
			"AllowOfflineAccess": true,
			"RequireClientSecret": false,
			"AllowAccessTokensViaBrowser": true
		}
	]
}
```
:::

::: tip
In this example we set the `RedirectUris` to both `https://localhost:2113` and `https://127.0.0.1:2113` so that you can log into the UI from either address.
:::

3. Pull and run the `kurrent-io/idsrv4` docker container:

```bash
docker pull ghcr.io/kurrent-io/idsrv4/idsrv4

docker run \
    --rm -it \
    -p 5000:5000 \                                            # HTTP port
    -p 5001:5001 \                                            # HTTPS port
    --volume $PWD/users.conf.json:/etc/idsrv4/users.conf \    # mount users file; required
    --volume $PWD/idsrv4.conf.json:/etc/idsrv4/idsrv4.conf \  # mount configuration file
    ghcr.io/kurrent-io/idsrv4/idsrv4
```

You should see the identity server start up and start listening on the ports `5000` and `5001`:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://[::]:5000
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://[::]:5001
```

4. Add the following configuration to your EventStoreDB config file `eventstore.conf`:

:::tabs
@tab eventstore.conf
```yaml
IntIp: 127.0.0.1
ExtIp: 127.0.0.1

# Licensing and certificates configuration omitted

AuthenticationType: oauth
OAuth:
  Audience: eventstore-client
  Issuer: https://localhost:5001/
  ClientId: eventstore-client
  ClientSecret: {client_secret}
  # For testing
  DisableIssuerValidation: true
  Insecure: true
```
:::

5. Start EventStoreDB. You should see the following logs indicating that OAuth is in use:

```
[INF] OAuthAuthenticationProvider    OAuthAuthentication 24.10.1.1499 plugin enabled.
...
[INF] OAuthAuthenticationPlugin      Obtaining auth token signing key from https://localhost:5001/
...
[INF] OAuthAuthenticationPlugin      Issuer signing keys have been retrieved. Key IDs: ["{key_id}"]
```

You should now be able to log into the UI with the configured user names and passwords.
