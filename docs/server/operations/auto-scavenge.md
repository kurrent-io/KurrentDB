---
order: 2
---

# Auto-Scavenge

<Badge type="info" vertical="middle" text="License Required"/>

The Auto-scavenge feature automatically schedules and coordinates _cluster scavenges_ which are composed of multiple _node scavenges_ run across all of the nodes in the cluster. The feature ensures that only one node scavenge is executed at a time in the cluster, and handles restarting or cancelling scavenges if a node is lost.

Cluster scavenges are configured to run on a set schedule with a CRON expression, and cluster scavenges can be paused and resumed if needed.

The Auto-scavenge does its best not to run the scavenge process on the leader node to minimise the impact a scavenge can have on your cluster. It does this by executing the scavenge on all of the non-leader nodes first. Once all of the other nodes have been scavenged, the leader of the cluster will resign before executing its own scavenge.

### Configuration

You require a [license key](../quick-start/installation.md#license-keys) to use this feature.

Auto-scavenge is automatically enabled by default when a valid license key is provided. It can be disabled with the following configuration. It will not run any scavenges until a schedule is set via the HTTP endpoint.

Refer to the [configuration guide](../configuration/README.md) for configuration mechanisms other than YAML.

```yaml
AutoScavenge:
  Enabled: false
```

Once the feature is enabled the server should log a similar message to the one below:

```
[18304, 1,23:01:53.335,INF] "AutoScavenge" "25.0.0.1673" plugin enabled.
```

::: note
Auto-scavenge cannot be enabled at the same time as `dev` mode or `mem-db`
:::

### HTTP endpoints

The Auto-scavenge feature adds several endpoints demonstrated with the following examples.

Replace `admin:changeit` with your credentials, and the url with the correct address for your server.

In a cluster the requests can be submitted to any node and they will be forwarded to the leader for processing.

#### Enabled endpoint

The `enabled` endpoint returns whether the feature is enabled.

Sample request:

```http
GET https://127.0.0.1:2113/auto-scavenge/enabled
Authorization: Basic admin:changeit
```

Sample response:

```json
{
  "enabled": true
}
```

Requires an authenticated user.

#### Status endpoint

The `status` endpoint returns information about the current state of the auto-scavenge process.

Sample request:

```http
GET https://127.0.0.1:2113/auto-scavenge/status
Authorization: Basic admin:changeit
```

Sample response:

```json
{
  "state": "Waiting",
  "schedule": "0 6 * * 6",
  "timeUntilNextCycle": "1.13:14:32.6299115"
}
```

The `state` field can be one of:

* `NotConfigured`: The auto-scavenge will not do anything until configured with a schedule.
* `Waiting`: Waiting until it is time to start the next cluster scavenge.
* `InProgress`: A cluster scavenge is currently in progress.
* `Pausing`: The auto-scavenge process is attempting to pause a node scavenge that is currently running.
* `Paused`: The auto-scavenbe process is paused and will not start any further node scavenges until resumed.

Requires an authenticated user.

#### Pause endpoint

The `pause` endpoint pauses the auto-scavenge process. A response of 200 OK indicates that the auto-scavenge process has been paused and any node scavenge that it was running has been stopped. A response of 202 ACCEPTED indicates that the auto-scavenge will not start any further node scavenges but is still attempting to stop a node scavenge that was already running.

Sample request:

```http
POST https://127.0.0.1:2113/auto-scavenge/pause
Authorization: Basic admin:changeit
```

Requires `$ops` or `$admin` roles.

#### Resume endpoint

After pausing, resume by using this endpoint. The cluster scavenge will continue from where it left off.

Sample request:

```http
POST https://127.0.0.1:2113/auto-scavenge/resume
Authorization: Basic admin:changeit
```

Requires `$ops` or `$admin` roles.

#### Configure endpoint

The schedule is a CRON expression.

```http
### configure to scavenge at 06:00 each Saturday
POST https://127.0.0.1:2113/auto-scavenge/configure
Content-Type: application/json
Authorization: Basic admin:changeit

{
    "schedule": "0 6 * * 6"
}
```

Requires `$ops` or `$admin` roles.

### Troubleshooting

Check that
- A valid license key is provided.
- The server is not running in `dev` mode.
- The server is not running in `mem-db` (i.e. with an in-memory database).
- A schedule has been set via the HTTP endpoint.

### Tutorial

[Learn how to schedule and manage Auto-Scavenge in KurrentDB through a tutorial.](/tutorials/Auto-Scavenge.md)
