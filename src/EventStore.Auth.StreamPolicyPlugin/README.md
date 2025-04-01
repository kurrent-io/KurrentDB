# Stream Policy Plugin

This plugin allows administrators to define stream access policies for EventStoreDB based on stream prefix.

## Enabling the plugin

The plugin can be enabled by setting the `Authorization:PolicyType` option to `streampolicy`.

You can do this by adding the following config to the `config` folder in the installation directory:

```
{
  "EventStore": {
    "Authorization": {
      "PolicyType": "streampolicy"
    }
  }
}
```

or by setting the environment variable:

```
EVENTSTORE__AUTHORIZATION__POLICY_TYPE="streampolicy"
```

You can check that the plugin is enabled by searching for the following log at startup:

```
[15708, 1,14:58:15.570,INF] "PolicyAuthorizationProvider" "24.6.0.0" plugin enabled.
```

## Stream Policies and Rules

Stream policies and Stream Rules are configured by events written to the `$policies` stream.

### Default Stream Policy

When the Stream Policy Plugin is run for the first time, it will create a default policy in the `$policies` stream.
The default policy does the following
- Grants public access to user streams (this excludes users in the `$ops` group).
- Restricts system streams to the `$admins` group.
- Grants public read and metadata read access to the default projection streams (`$ce`, `$et`, `$bc`, `$category`, `$streams`).

```
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

**Note:** Operations users in the `$ops` group are excluded from the `$all` group and does not have access to user streams by default.

### Custom Stream Policies

You can create a custom stream policy by writing an event with event type `$policy-updated` to the `$policies` stream.

Define your custom policy in the `streamPolicies`, e.g:

```
"streamPolicies": {
    "customPolicy": {
        "$r": ["ouro", "readers"],
        "$w": ["ouro"],
        "$d": ["ouro"],
        "$mr": ["ouro"],
        "$mw": ["ouro"]
    },
    // Default policies truncated (full version defined below)
}
```

And then add an entry to `streamRules` to specify the stream prefixes which should use the custom policy. You can apply the same policy to multiple streams by defining multiple stream rules. e.g:

```
"streamRules": [{
    "startsWith": "account",
    "policy": "customPolicy"
}, {
    "startsWith": "customer",
    "policy": "customPolicy"
}]
```

You still need to specify default stream rules when you update the `$policies` stream. So the above example would look like this in full:

```
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
        {
            "projectionsDefault": {
			"$r": ["$all"],
			"$w": ["$admins"],
			"$d": ["$admins"],
			"$mr": ["$all"],
			"$mw": ["$admins"]
		},
        "customPolicy": {
            "$r": ["$all"],
            "$w": ["$all"],
            "$d": ["$all"],
            "$mr": ["$all"],
            "$mw": ["$all"]
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

**Note:** If a policy update is invalid, it will not be applied and an error will be logged. EventStoreDB will continue running with the previous valid policy in place.

### Stream Policy Schema

#### Policy

| Key                   | Type                                  | Description | Required |
|-----------------------|---------------------------------------|-------------|----------|
| `streamPolicies`      | `Dictionary<string, AccessPolicy>`    | The named policies which can be applied to streams. | Yes |
| `streamRules`         | `StreamRule[]`                        | An array of rules to apply for stream access.  The ordering is important, and the first match wins. | Yes |
| `defaultStreamRules`  | `DefaultStreamRules`                  | The default policies to apply if no other policies are defined for a stream | Yes |

#### AccessPolicy

| Key   | Type	    | Description	| Required |
|-------|-----------|---------------|----------|
| `$r`  | string[]  | User and group names that are granted stream read access. | Yes |
| `$w`  | string[]  | User and group names that are granted stream write access. | Yes |
| `$d`  | string[]  | User and group names that are granted stream delete access. | Yes |
| `$mr` | string[]  | User and group names that are granted metadata stream read access. | Yes |
| `$mw` | string[]  | User and group names that are granted metadata stream write access. | Yes |

**Note:** Having metadata read or metadata write access to a stream does not grant read or write access to that stream.

#### DefaultStreamRules

|Key                | Type           | Description   | Required |
|-------------------|----------------|---------------|----------|
| `userStreams`     | `AccessPolicy` | The default policy to apply for non-system streams. | Yes |
| `systemStreams`   | `AccessPolicy` | The default policy to apply for system streams. | Yes |

#### StreamRule

| Key           | Type     | Description   | Required |
|---------------|----------|---------------|----------|
| `startsWith`  | `string` | The stream prefix to apply the rule to. | Yes |
| `policy`      | `string` | The name of the policy to enforce for streams that match this prefix. | Yes |

