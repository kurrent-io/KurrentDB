# TCP API Plugin
The external TCP API had been removed since v24.2, which meant that any external clients using the TCP API will no longer work with EventStoreDB versions 24.2.0 and onwards.
The TCP API Plugin allows users to integrate the external TCP API into EventStoreDB, thus allowing the users to use their existing TCP Client functionality with future versions of EventStoreDB.
To enable the TCP API Plugin, you will need to download the plugin and add it to the ./plugins folder in your EventStoreDB installation directory.

You then need to add the configuration to enable the plugin to a json file in the {installation_directory}/config directory.
For example, add the following configuration to ./config/tcp-plugin-config.json file within the EventStoreDB installation directory:

```
{
	"EventStore": {
		"TcpPlugin": {
			"EnableExternalTcp": true
		}
	}
}
```

The name of the above json config file is not important. What is important is that the json config file should be in the {installation_directory}/config directory.
Once the plugin is installed and enabled the server should log a similar message to the one below:

```
[11212, 1,18:44:34.070,INF] "TcpApi" "24.6.0.0" plugin enabled.
```

You should then be able to connect and perform operations in a similar manner as with the legacy TCP client on EventStoreDB v23.10 and before.

## Other parameters

The following options can be set using the json config as mentioned above :
1. NodeHeartbeatTimeout. Default value : 1000
2. NodeHeartbeatInterval. Default value : 2000
3. NodeTcpPort. Default value : 1113

The default values for the above can be overridden by the following example json config :

```
{
	"EventStore": {
		"TcpPlugin": {
			"EnableExternalTcp": true,
			"NodeTcpPort": 1113,
			"NodeTcpPortAdvertiseAs": 1113,
			"NodeHeartbeatInterval": 2000,
			"NodeHeartbeatTimeout": 1000
		}
	}
}
```

## Troubleshooting

### Plugin not loaded
The plugin has to be located in a subdirectory of the server's plugins directory. To check this:

1. Go to the installation directory of the server, the directory containing the EventStoreDb executable.
2. In this directory there should be a directory called plugins, create it if this is not the case.
3. The plugins directory should have a subdirectory for the plugin, for instance called EventStore.TcpPlugin but this could be any name. Create it if it doesn't exist.
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
[11212, 1,18:44:30.504,INF] Loaded SubsystemsPlugin plugin: "tcp-api" "24.6.0.0".
```

### Plugin not started
The plugin has to be configured to start in order to be able to be integrated into EventStoreDB server.
If you see the following log it means the plugin was found but was not started:

```
[ 5104, 1,19:03:13.807,INF] "TcpApi" "24.6.0.0" plugin disabled. "Set 'EventStore:TcpPlugin:EnableExternalTcp' to 'true' to enable"
```

Ensure the plugin is enabled by adding a json config file to the {installation_directory}/config directory as mentioned above.
When the plugin starts correctly you should see the following log:

```
[11212, 1,18:44:34.070,INF] "TcpApi" "24.6.0.0" plugin enabled.
```
