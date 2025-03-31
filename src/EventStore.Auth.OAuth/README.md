# EventStore OAuth Plugin

The OAuth plugin allows EventStoreDB to connect to an identity server and authenticate users based on a JWT rather than username and password.

Access tokens can contain a "role" claim, which will be mapped to the user's group for authorization.

**Note** With default auth, EventStoreDB treats the username as a "role" in addition to the groups they are in. If you want to have the same behaviour with the OAuth plugin, you will need to add the user's username as an additional role claim.

## Configuration

You can enable the OAuth plugin by setting the following options in you eventstore config file:
- Set the `AuthenticationType` to `oauth`
- Provide an oath config file with the `AuthenticationConfig` option.

For example:

```
---
MemDb: true
EnableAtomPubOverHttp: true

CertificateFile: {node.crt}
CertificatePrivateKeyFile: {node.key}
TrustedRootCertificatesPath: {ca}

AuthenticationType: oauth
AuthenticationConfig: es-oauth.conf
```

Create a separate configuration file `es-oauth.conf` (the name is not important) to configure the OAuth plugin.
The configuration options are:

| Name 			 | Type	| Required?	| Default 	|Description |
|------------------------|----------|-----------|-----------|------------|
| Audience 			 | string 	| Y 		| - 		| The audience used in your identity provider. |
| Issuer 			 | string 	| Y 		| - 		| The issuer endpoint for your identity provider. |
| ClientId			 | string 	| N 		| "" 		| The id of the client configured in the identity provider. |
| ClientSecret 		 | string 	| N 		| "" 		| The client secret configured in the identity provider. |
| DisableIssuerValidation| bool 	| N 		| false 	| Disable issuer validation for testing purposes. |
| Insecure 			 | bool 	| N 		| false 	| Whether to validate the certificates for the identity provider. This is not related to `Insecure` in the EventStoreDB configuration. |

For example:
```
---
OAuth:
  Audience: eventstore-client
  Issuer: https://localhost:5001/
  ClientId: eventstore-client
  ClientSecret: K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=
  # For testing
  DisableIssuerValidation: true
  Insecure: true
```

## Set up a local identity server

We have an [Identity Server 4 docker container](https://github.com/EventStore/idsrv4) for testing.

1. Create a `users.conf.json` file to configure the users for your test.

<details>
	<summary>users.conf.json</summary>

```
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
		}, {
			"type": "given_name",
			"value": "Alice Smith"
		}]
	}, 	{
		"subjectId": "2",
		"username": "operator",
		"password": "password",
		"claims": [{
			"type": "role",
			"value": "$ops"
		}]
	}, 	{
		"subjectId": "3",
		"username": "ouro",
		"password": "password",
		"claims": [{
			"type": "role",
			"value": "custom"
		}]
	}, 	{
		"subjectId": "4",
		"username": "user",
		"password": "password",
		"claims": []
	}
]
```
</details>

This creates the following users:

| Username 		| Password 		| Roles |
|---------------|---------------|-------|
| `admin`		| `password` 	| `$admins`, `$ops` |
| `operator` 	| `password` 	| `$ops` |
| `ouro`		| `password`	| `custom` |
| `user`		| `password`	| None |

2. Create an identity server config file, `idsrv4.conf.json`.

You need to configure the following:

- A role claim must be added to the token: `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`
- The grant types of `password` and `authorization_code` must be allowed.
- A redirect uri of `{eventstore_server_ip}/oauth/callback` must be allowed for the legacy UI to function.

<details>
	<summary>idsrv4.conf.json</summary>

```
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
				"authorization_code"
			],
			"ClientSecrets": [
				{
					"Value": "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols="
				}
			],
			"AllowedScopes": [
				"streams",
				"openid",
				"profile",
			],
			"RedirectUris": ["https://localhost:2113/oauth/callback"],
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
</details>

3. Pull and run the `kurrent-io/idsrv4` docker container:

```
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

You should now be able to log into the UI with the configured users.

### Local testing with dotnet gRPC client

A full sample can be found in [EventStore.Auth.OAuth.Tests](https://github.com/EventStore/EventStore.CommercialHA/blob/master/src/EventStore.Auth.OAuth.Tests/OAuthAuthenticationGrpcIntegrationTests.cs).

Install the following nuget packages:

- EventStore.Client.Grpc.Streams
- IdentityModel
- System.IdentityModel.Tokens.Jwt

You need to first request an access token from the Idp, and then provide this as the credentials for your requests to EventStoreDB.

<details>
	<summary>dotnet gRPC sample application</summary>

```
using System.Net;
using EventStore.Client;
using IdentityModel.Client;

const int IdpPort = 5001;
const string ClientId = "eventstore-client";
const string ClientSecret = "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=";
const string RequestedScopes = "openid streams offline_access";

var token = await GetToken("admin", "password");
var settings = EventStoreClientSettings.Create("esdb://localhost:2113?tlsVerifyCert=false");
settings.DefaultCredentials = new UserCredentials(token);
var client = new EventStoreClient(settings);

try
{
    var res = client.ReadAllAsync(Direction.Backwards, Position.End, 10);
    await foreach (var evnt in res)
    {
        Console.WriteLine($"{evnt.OriginalEventNumber}@{evnt.OriginalStreamId}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Could not read stream: {ex}");
}

async ValueTask<string> GetToken(string username, string password)
{
    var httpClient = new HttpClient(new SocketsHttpHandler
    {
        SslOptions =
        {
            RemoteCertificateValidationCallback = delegate { return true; }
        }
    }, true)
    {
        BaseAddress = new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Port = IdpPort
        }.Uri
    };

    var request = new DiscoveryDocumentRequest();
    var discoveryDocument = await httpClient.GetDiscoveryDocumentAsync(request);
    if (discoveryDocument?.HttpResponse == null)
    {
        throw new Exception("Health check not available yet");
    }

    if (discoveryDocument.HttpStatusCode != HttpStatusCode.OK)
    {
        throw new Exception($"Health check failed with status code {discoveryDocument.HttpStatusCode}");
    }

    var response = await httpClient.RequestPasswordTokenAsync(new PasswordTokenRequest
    {
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        UserName = username,
        Password = password,
        Address = discoveryDocument.TokenEndpoint,
        Scope = RequestedScopes,
    });
    if (response.Exception != null)
    {
        throw response.Exception;
    }

    return response.AccessToken ?? "";
}
```
</details>
