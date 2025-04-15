# AutoScavenge Plugin

The Autoscavenge plugin automatically schedules _cluster scavenges_ which are composed of multiple _node scavenges_. Only one node scavenge
can be executed at a time in the cluster. The Autoscavenge plugin allows to schedule said _cluster scavenges_. In order to use the plugin,
it needs to be present in a subdirectory in the `./plugins` folder in your EventStoreDB installation directory.

You then need to add the configuration to enable the plugin to a json file in the `{installation_directory}/config` directory. Note, the
plugin doesn't support a server running in dev mode.
For example, add the following configuration to `./config/auto-scavenge-plugin-config.json` file within the EventStoreDB installation directory:

```json
{
  "EventStore": {
    "AutoScavenge": {
      "Enabled": true
    }
  }
}
```

The name of the above json config file is not important. What is important is that the json config should be in the `{installation_directory}/config` directory.
Once the plugin is installed and enabled the server should log a similar message to the one below:

```
[18304, 1,23:01:53.335,INF] "AutoScavenge" "24.10.0" plugin enabled.
```

## HTTP endpoints

The Autoscavenge plugin exposes HTTP endpoints.

* `GET /auto-scavenge/enabled`: Tells if the Autoscavenge plugin is enabled or not.
* `GET /auto-scavenge/status`: Tells the current status of the cluster auto scavenge process. You can know for example if it's running or when the next process will run.
* `POST /auto-scavenge/configure`: Configure the cluster auto scavenge process schedule. The expected json payload is the following:
```json
{
  "schedule": "{ Valid standard CRON expression }"
}
```
* `POST /auto-scavenge/pause`: Pauses the cluster auto scavenge process. No payload is needed.
* `POST /auto-scavenge/resume`: Resumes the cluster auto scavenge process. No payload is needed.

Unlike the `GET /auto-scavenge/enabled` endpoint, all the other endpoints require a user to be authenticated and to have either an ops or an admin role.

## Troubleshooting

### Plugin not loaded
The plugin has to be located in a subdirectory of the server's plugins directory. To check this:

1. Go to the installation directory of the server, the directory containing the EventStoreDB executable.
2. In this directory there should be a directory called plugins, create it if this is not the case.
3. The plugins directory should have a subdirectory for the plugin, for instance called `EventStore.AutoScavenge` but this could be any name. Create it if it doesn't exist.
4. The binaries of the plugin should be located in the subdirectory which was checked in the previous step.

You can verify which plugins have been found and loaded by looking for the following logs:

```
[11212, 1,18:44:30.420,INF] Plugins path: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins"
[11212, 1,18:44:30.420,INF] Adding: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins" to the plugin catalog.
[11212, 1,18:44:30.422,INF] Adding: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins\\EventStore.Licensing" to the plugin catalog.
[11212, 1,18:44:30.432,INF] Adding: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins\\EventStore.POC.ConnectedSubsystemsPlugin" to the plugin catalog.
[11212, 1,18:44:30.433,INF] Adding: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins\\EventStore.POC.ConnectorsPlugin" to the plugin catalog.
[11212, 1,18:44:30.436,INF] Adding: "C:\\EventStore.CoupledPlugins\\EventStore\\src\\EventStore.ClusterNode\\bin\\Debug\\net8.0\\plugins\\EventStore.TcpPlugin" to the plugin catalog.
[11212, 1,18:44:30.479,INF] Loaded SubsystemsPlugin plugin: "licensing" "24.6.0.0".
[11212, 1,18:44:30.483,INF] Loaded SubsystemsPlugin plugin: "connected" "0.0.5".
[11212, 1,18:44:30.496,INF] Loaded ConnectedSubsystemsPlugin plugin: "connectors" "0.0.5".
[11212, 1,18:44:30.496,INF] Loaded ConnectedSubsystemsPlugin plugin: "auto-scavenge" "24.10.0".
```

### Plugin not started
The plugin has to be configured and the server must not run in dev mode to start in order to be able to be integrated into EventStoreDB server.
If you see the following log it means the plugin was found but was not started:

```
[ 5104, 1,19:03:13.807,INF] "AutoScavenge" "24.10.0" plugin disabled. "Set 'EventStore:AutoScavenge:Enabled' to 'true' and don't run the server in dev mode to enable the auto-scavenge plugin"
```

Ensure the plugin is enabled by adding a json config file to the `{installation_directory}/config` directory as mentioned above.
When the plugin starts correctly you should see the following log:

```
[11212, 1,18:44:34.070,INF] "AutoScavenge" "24.10.0" plugin enabled.
```
