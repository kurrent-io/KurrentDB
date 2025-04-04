---
title: User authorization
order: 3
---

# Authorization

Authorization governs what an authenticated user can access and what actions they can perform, based on that user's roles.

## Roles

Users are granted roles by the [authentication mechanism](./user-authentication.md). These roles are then used to determine what operations, endpoints, and streams a user has access to.

KurrentDB provides the `$admins` and `$ops` roles by default, granted respectively to the `admin` and `ops` default users. Custom roles can also be created.

### Admins role

The `$admins` role is granted to the `admin` user by default, and authorizes the user to do the following:

- Read all streams, including `$all` and system streams.
- Manage users and reset their passwords.
- Perform all administrative operations on a node or cluster, such as scavenges, restarts, and triggering leader elections.
- Manage projections and persistent subscriptions.
- View statistics for the node, subscriptions, or projections

You can create additional admin users by assigning the `$admins` group to a user.

::: note
The `$admins` role bypasses all stream access checks such as [ACLs](#access-control-lists) or [stream policies](#stream-policy-authorization).
:::

### Ops role

The `$ops` role is granted to the `ops` user by default. This role grants similar permissions to the `$admins` role, but without the user management and stream access.

The `$ops` role authorizes the user to do the following:

- Perform maintenance operations on a node or cluster, such as scavenges, restarts, and triggering leader elections.
- Manage projections and persistent subscriptions.
- View statistics for the node, subscriptions, or projections
- Stream access is determined by the [authorization mechanism used](#stream-access)

You can create additional ops users by assigning the `$ops` group to a user.

### Custom roles

Custom roles are any role assigned to an authenticated user, outside of the `$admins` and `$ops` roles.
These can be groups assigned to the user, their username, or a mapping from the authentication mechanism used. These roles can be anything, but typically describe the group or tenant that a user is part of. Example of custom roles are `accounting`, `sales`, or `tenant-123`.

Users with custom (or no) roles are authorized to do the following:

- View statistics for the node, subscriptions, or projections
- Stream access is determined by the [authorization mechanism used](#stream-access)

::: tip
Users managed in KurrentDB are always granted a role that matches their username.
:::

## Stream access

Stream access covers what actions a user can perform on a stream within KurrentDB. These actions are:

| Name              | Key   | Description |
|-------------------|-------|-------------|
| Read              | `$r`  | The permission to read from a stream. |
| Write             | `$w`  | The permission to write to a stream. |
| Delete            | `$d`  | The permission to delete a stream. |
| Metadata read     | `$mr` | The permission to read the metadata associated with a stream. |
| Metadata write    | `$mw` | The permission to write the metadata associated with a stream. |

KurrentDB supports these methods of authorizing user access to streams:
- [Access control lists](#access-control-lists) to restrict user access to streams based on metadata in each stream.
- [Stream policies](#stream-policy-authorization) to restrict user acces to streams based on configured policies.

### Access control lists

By default, authenticated users have access to all non-system streams in the KurrentDB database. You can use Access Control Lists (ACLs) to set up more granular access control. In fact, the default access
level is also controlled by a special ACL, which is called the [default ACL](#default-acl).

#### Stream ACL

KurrentDB keeps the ACL of a stream in the stream metadata as JSON with the below definition:

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

#### Default ACL

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

@[code{curl}](@samples/default-settings.sh)

The `$userStreamAcl` controls the default ACL for user streams, while all system streams use
the `$systemStreamAcl` as the default.

::: tip 
Members of `$admins` always have access to everything, you cannot remove this permission.
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

### Stream policy authorization

<Badge type="info" vertical="middle" text="License Required"/>

This allows administrators to define stream access policies for KurrentDB based on stream prefix.

#### Enabling

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

The stream access policy is configured by the last event in the `$authorization-policy-settings` stream.

Enable stream policies by appending an event of type `$authorization-policy-changed` to the `$authorization-policy-settings` stream:

::: tabs
@tab HTTP
```http
POST https://localhost:2113/streams/$authorization-policy-settings
Authorization: Basic admin changeit
Content-Type: application/json
Kurrent-EventId: 11887e82-9fb4-4112-b937-aea895b32a4a
Kurrent-EventType: $authorization-policy-changed

{
    "streamAccessPolicyType": "streampolicy"
}
```

@tab Powershell
```powershell
$JSON = @"
{
    "streamAccessPolicyType": "streampolicy"
}
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -H "Kurrent-EventId: 11887e82-9fb4-4112-b937-aea895b32a4b" `
  -H "Kurrent-EventType: `$authorization-policy-changed" `
  -u "admin:changeit" `
  -d $JSON `
  https://localhost:2113/streams/%24authorization-policy-settings
```

@tab Bash
```bash
JSON='{
    "streamAccessPolicyType": "streampolicy"
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -H "Kurrent-EventId: 814f6d67-515c-4bd6-a6d6-8a0235ec719a" \
  -H "Kurrent-EventType: \$authorization-policy-changed" \
  -u "admin:changeit" \
  -d "$JSON" \
  https://localhost:2113/streams/%24authorization-policy-settings
```
:::

And disable stream policies by setting the stream access policy type back to `acl`:

::: tabs
@tab HTTP

```http
### Configure cluster to use acls
POST https://localhost:2113/streams/$authorization-policy-settings
Authorization: Basic admin changeit
Content-Type: application/json
Kurrent-EventId: d07a6637-d9d0-43b7-8f48-6a9a8800af12
Kurrent-EventType: $authorization-policy-changed

{
    "streamAccessPolicyType": "acl"
}
```

@tab Powershell
```Powershell
$JSON = @"
{
    "streamAccessPolicyType": "acl"
}
"@ `

curl.exe -X POST `
  -H "Content-Type: application/json" `
  -H "Kurrent-EventId: 3762c89e-d12e-4a01-83bb-781459c93ebc" `
  -H "Kurrent-EventType: `$authorization-policy-changed" `
  -u "admin:changeit" `
  -d $JSON `
  https://localhost:2113/streams/%24authorization-policy-settings
```
@tab Bash
```bash
JSON='{
    "streamAccessPolicyType": "acl"
}'

curl -X POST \
  -H "Content-Type: application/json" \
  -H "Kurrent-EventId: d6b3a316-2493-4bcd-9019-e02b5ae4eec3" \
  -H "Kurrent-EventType: \$authorization-policy-changed" \
  -u "admin:changeit" \
  -d "$JSON" \
  https://localhost:2113/streams/%24authorization-policy-settings
```

:::

When stream policies are enabled, you should see the following logs:

You can check that the feature is enabled by searching for the following log at startup:

```
[22860,28,17:30:42.247,INF] StreamBasedAuthorizationPolicyRegistry New Authorization Policy Settings event received
[22860,28,17:30:42.249,INF] StreamBasedAuthorizationPolicyRegistry Starting factory streampolicy
...
[22860,28,17:30:42.264,INF] StreamBasedPolicySelector      Existing policy found in $policies stream (0)
[22860,28,17:30:42.269,INF] StreamBasedPolicySelector      Successfully applied policy
[22860,28,17:30:42.271,INF] StreamBasedPolicySelector      Subscribing to $policies at 0
```

If you do not have a valid license, stream policies will log the following error and will not start. KurrentDB will continue to run with the current policy type:

```
[22928, 1,17:31:56.879,INF] StreamPolicySelector           Stream Policies plugin is not licensed, stream policy authorization may not be enabled.
...
[22928,28,17:31:57.030,ERR] StreamPolicySelector           Stream Policies plugin is not licensed, cannot enable Stream policies
[22928,28,17:31:57.030,ERR] StreamBasedAuthorizationPolicyRegistry Failed to enable policy selector plugin streampolicy. Authorization settings will not be applied
```

::: note
If you enable the Stream Policy feature, KurrentDB will not enforce [stream ACLs](#access-control-lists).
:::

#### Overriding the default

If the `$authorization-policy-settings` stream is empty and there is no configuration, KurrentDB will default to using ACLs.

If you would rather default to stream policies, you can do this by setting the `Authorization:DefaultPolicyType` option to `streampolicy`.

::: note
Any events in the `$authorization-policy-settings` stream take precedence over the `Authorization:DefaultPolicyType` setting.
:::

You can do this by adding the following to the server configuration file:

```yaml
Authorization:
  DefaultPolicyType: streampolicy
```

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

::: warning
If the policy type configured in `Authorization:DefaultPolicyType` is not present at startup, KurrentDB will not start.
:::

#### Fallback stream access policy

If none of the events in the `$authorization-policy-settings` stream are valid, or if the stream policy plugin could not be started (for example, due to it being unlicensed), KurrentDB will fall back to restricted access.

This locks down all stream access to admins only to prevent KurrentDB from falling back to a more permissive policy such as ACLs.

To recover from this, either fix the issue preventing the plugin from loading, or set the stream access policy type to a different policy such as ACL:

```json
{
    "streamAccessPolicyType": "acl"
}
```

#### Stream policies and rules

Stream policies and Stream Rules are configured by events written to the `$policies` stream.

##### Default stream policy

KurrentDB creates default stream policies by adding a default policy event in the `$policies` stream when the feature is enabled for the first time. The default policies specified in that event include:

- Restricts [system streams](../features/streams.md#system-events-and-streams) to the `$admins` group.
- Grants users outside of the `$ops` group access to user streams.
- Grants users outside of the `$ops` group read access to the [default projection streams](../features/streams.md#projections-events-and-streams) (`$ce`, `$et`, `$bc`, `$category`, `$streams`) and their metadata.

KurrentDB will log when the default policy is created:

```
[31124,16,11:18:15.099,DBG] StreamBasedPolicySelector Successfully wrote default policy to stream $policies.
```

::: details Click here to see the default policy

```json
{
  "streamPolicies": {
    "publicDefault": {
      "$r": ["$all"],
      "$w": ["$all"],
      "$d": ["$all"],
      "$mr": ["$all"],
      "$mw": ["$all"]
    },
    "adminsDefault": {
      "$r": ["$admins"],
      "$w": ["$admins"],
      "$d": ["$admins"],
      "$mr": ["$admins"],
      "$mw": ["$admins"]
    },
    "projectionsDefault": {
      "$r": ["$all"],
      "$w": ["$admins"],
      "$d": ["$admins"],
      "$mr": ["$all"],
      "$mw": ["$admins"]
    }
  },
  "streamRules": [
    {
      "startsWith": "$et-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$ce-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$bc-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$category-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$streams",
      "policy": "projectionsDefault"
    }
  ],
  "defaultStreamRules": {
    "userStreams": "publicDefault",
    "systemStreams": "adminsDefault"
  }
}
```

:::

::: note
Operations users in the `$ops` group are excluded from the `$all` group and do not have access to user streams by default.
:::

##### Custom stream policies

You can create a custom stream policy by writing an event with event type `$policy-updated` to the `$policies` stream.

We recommend that you start with the default policy shown above, and add your own policies and stream rules to it.

Define policies by adding one or more entries to the `streamPolicies` json object, with the key being your policy name, and the value being an [access policy](#accesspolicy):

```json
"streamPolicies": {
  "{your_policy_name}": {
    "$r": ["{roles_with_read_access}"],
    "$w": ["{roles_with_write_access}"],
    "$d": ["{roles_with_delete_access}"],
    "$mr": ["{roles_with_metadata_read_access}"],
    "$mw": ["{roles_with_metadata_write_access}"],
  },
  // Default policies truncated
}
```

For example, this policy, named `customPolicy`, grants read, write, delete, metadata read, and metadata write access to users with the `ouro` role, and only grants read access to users in the `readers` role:

```json
"streamPolicies": {
    "customPolicy": {
        "$r": ["ouro", "readers"],
        "$w": ["ouro"],
        "$d": ["ouro"],
        "$mr": ["ouro"],
        "$mw": ["ouro"]
    },
    // Default policies truncated (full version shown below)
}
```

Define which policies apply to which stream prefix by adding a [stream rule](#streamrule) entry to the `streamRules` object:

```json
"streamRules": [{
  "startsWith": "{stream_prefix}",
  "policy": "{your_policy_name}"
},
// Default stream rules truncated
]
```

For example, these stream rules apply the `customPolicy` defined above to streams starting with `account` and `customer`:

```json
"streamRules": [{
    "startsWith": "account",
    "policy": "customPolicy"
}, {
    "startsWith": "customer",
    "policy": "customPolicy"
}
// Default stream rules truncated (full version shown below)
]
```

::: tip
You can apply the same policy to multiple streams by defining multiple stream rules.
:::

Define which policies apply to user and system streams by default by updating the `defaultStreamRules` object:

```json
"defaultStreamRules": {
  "userStreams": "{default_users_policy_name}",
  "systemStreams": "{default_system_policy_name}"
}
```

For example, to keep using the system defaults:

```json
"defaultStreamRules": {
  "userStreams": "publicDefault",
  "systemStreams": "adminsDefault"
}
```

::: tip
Make sure that the default policies are included in the `streamPolicies` object.
:::

To update the policies, append an event containing the json created in the above steps to the `$policies` stream with the `$policy-updated` event type:

::: tabs
@tab HTTP
```http
POST https://localhost:2113/streams/$policies
Authorization: Basic admin changeit
Content-Type: application/json
Kurrent-EventId: b04a64bf-4ac2-4f36-a856-6da9b63e64b7
Kurrent-EventType: $policy-updated

< ./customPolicy.json
```

@tab Powershell
```powershell
curl.exe -X POST `
  -H "Content-Type: application/json" `
  -H "Kurrent-EventId: 8bc7c581-0f07-4987-bfcb-ab8f9404ca34" `
  -H "Kurrent-EventType: `$policy-updated" `
  -u "admin:changeit" `
  -d @customPolicy.json `
  https://localhost:2113/streams/%24policies
```

@tab Bash
```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "Kurrent-EventId: cb6a722d-c775-4831-929b-86870560c68e" \
  -H "Kurrent-EventType: \$policy-updated" \
  -u "admin:changeit" \
  -d @customPolicy.json \
  https://localhost:2113/streams/%24policies

```

@tab customPolicy.json
```json
{
  "streamPolicies": {
    "customPolicy": {
      "$r": ["ouro", "readers"],
      "$w": ["ouro"],
      "$d": ["ouro"],
      "$mr": ["ouro"],
      "$mw": ["ouro"]
    },
    "publicDefault": {
      "$r": ["$all"],
      "$w": ["$all"],
      "$d": ["$all"],
      "$mr": ["$all"],
      "$mw": ["$all"]
    },
    "adminsDefault": {
      "$r": ["$admins"],
      "$w": ["$admins"],
      "$d": ["$admins"],
      "$mr": ["$admins"],
      "$mw": ["$admins"]
    },
    "projectionsDefault": {
      "$r": ["$all"],
      "$w": ["$admins"],
      "$d": ["$admins"],
      "$mr": ["$all"],
      "$mw": ["$admins"]
    }
  },
  "streamRules": [
    {
      "startsWith": "account",
      "policy": "customPolicy"
    },
    {
      "startsWith": "customer",
      "policy": "customPolicy"
    },
    {
      "startsWith": "$et-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$ce-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$bc-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$category-",
      "policy": "projectionsDefault"
    },
    {
      "startsWith": "$streams",
      "policy": "projectionsDefault"
    }
  ],
  "defaultStreamRules": {
    "userStreams": "publicDefault",
    "systemStreams": "adminsDefault"
  }
}
```

:::

::: note
If a policy update is invalid, it will not be applied and an error will be logged. KurrentDB will continue running with the previous valid policy in place.
:::

##### Stream policy schema

###### Policy

| Key                   | Type                                  | Description | Required |
|-----------------------|---------------------------------------|-------------|----------|
| `streamPolicies`      | `Dictionary<string, AccessPolicy>`    | The named policies which can be applied to streams. | Yes |
| `streamRules`         | `StreamRule[]`                        | An array of rules to apply for stream access.  The ordering is important, and the first match wins. | Yes |
| `defaultStreamRules`  | `DefaultStreamRules`                  | The default policies to apply if no other policies are defined for a stream | Yes |

###### AccessPolicy

| Key   | Type	    | Description	| Required |
|-------|-----------|---------------|----------|
| `$r`  | string[]  | User and group names that are granted stream read access. | Yes |
| `$w`  | string[]  | User and group names that are granted stream write access. | Yes |
| `$d`  | string[]  | User and group names that are granted stream delete access. | Yes |
| `$mr` | string[]  | User and group names that are granted metadata stream read access. | Yes |
| `$mw` | string[]  | User and group names that are granted metadata stream write access. | Yes |

::: note
Having metadata read or metadata write access to a stream does not grant read or write access to that stream.
:::

###### DefaultStreamRules

|Key                | Type           | Description   | Required |
|-------------------|----------------|---------------|----------|
| `userStreams`     | `AccessPolicy` | The default policy to apply for non-system streams. | Yes |
| `systemStreams`   | `AccessPolicy` | The default policy to apply for system streams. | Yes |

###### StreamRule

| Key           | Type     | Description   | Required |
|---------------|----------|---------------|----------|
| `startsWith`  | `string` | The stream prefix to apply the rule to. | Yes |
| `policy`      | `string` | The name of the policy to enforce for streams that match this prefix. | Yes |


#### Tutorial

[Learn how to set up and use Stream Policy Authorization in KurrentDB through a tutorial.](https://developers.kurrent.io/tutorials/Stream_Policy_Authorization.md)


#### Troubleshooting

##### License is invalid or not found

This feature requires a license to use. If a license is not found, or the provided license is invalid, you will see the following log:

```
[INF] StreamPolicySelector           Stream Policies plugin is not licensed, stream policy authorization cannot be enabled.
```

Trying to enable the feature will give you the following errors, and the previous or default policy authorization settings will be used:

```
[ERR] StreamPolicySelector           Stream Policies plugin is not licensed, cannot enable Stream policies
[ERR] StreamBasedAuthorizationPolicyRegistry Failed to enable policy selector plugin streampolicy. Authorization settings will not be applied
```

If the `DefaultPolicyType` is set to `streampolicy`, the [fallback policy](#fallback-stream-access-policy) will be used and stream access will be restricted to admins only:

```
[WRN] StreamBasedAuthorizationPolicyRegistry Could not load authorization policy settings. Restricting access to admins only.
```

#### The `$authorization-policy-settings` stream has been deleted

If the `$authorization-policy-settings` stream has been deleted (including hard deleted), the default policy type will be used, and KurrentDB will log the following:

```
[WRN] StreamBasedAuthorizationPolicyRegistry Authorization policy settings stream $authorization-policy-settings has been deleted.
[INF] KurrentDB                     No existing authorization policy settings were found in $authorization-policy-settings. Using the default
```

#### New authorization settings could not be applied

If an invalid update is made to the `$authorization-policy-changed` stream, you will see the following log and the settings will not be updated.

```
[WRN] StreamBasedAuthorizationPolicyRegistry New authorization settings could not be applied. Settings were not updated.
```

This can happen because:

1. The event type is not `$authorization-policy-changed`:

```
[ERR] StreamBasedAuthorizationPolicyRegistry Invalid authorization policy settings event. Expected event type $authorization-policy-changed but got {invalid_event_type}
```

2. The event is not valid json:

```
[ERR] StreamBasedAuthorizationPolicyRegistry Could not parse authorization policy settings
```

3. The stream access policy type was not found. Ensure that the policy type is set to `acl` or `streampolicy`:

```
[ERR] StreamBasedAuthorizationPolicyRegistry Could not find policy not-found in registered authorization policy plugins.
```

#### Could not parse policy

If an invalid update is made to the `$policies` stream, you will see the following log and the policies will not be updated.

```
[ERR] StreamBasedPolicySelector      Could not parse policy
```

This can happen because:

1. A stream rule references a policy that does not exist. Ensure that the policy is defined in the [streamPolicies](#custom-stream-policies) json object:

```
[ERR] StreamPolicySelector           Stream rule for prefix: {stream_prefix} refers to an undefined policy: {not_found_policy}
```

2. An [access policy](#accesspolicy) is missing a key. Ensure that each access key (`$r`, `$w`, `$d`, `$mr`, `$md`) is defined:

```
[ERR] StreamPolicySelector           Error while deserializing new policy
System.ArgumentNullException: Value cannot be null. (Parameter 'Deleters cannot be null')
```

3. A [stream rule](#streamrule) has an empty prefix:

```
[ERR] StreamPolicySelector           Error while deserializing new policy
System.ArgumentNullException: Value cannot be null. (Parameter 'StartsWith cannot be null or empty')
```

4. The event type is not `$policy-updated`:

```
[ERR] StreamPolicySelector           Expected event type: $policy-updated but was {invalid_event_type}
```
