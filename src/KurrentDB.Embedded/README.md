# KurrentDB.Embedded

Runs a single-node KurrentDB instance inside your own process, reachable over a UNIX domain socket located in the database directory.

The regular KurrentDB .NET gRPC client can be used to access the socket (see the sample).

No ports are listened on by default.

A connection over the UNIX domain socket is automatically authenticated as the system account without need for credentials, and has full administrative access to the database.
The socket and data both need suitable file system permissions; these get configured to be accessible by owner only.

## Sample

https://github.com/kurrent-io/KurrentDB/tree/v26.3.0-alpha.6/src/KurrentDB.Embedded.Sample

## Limitations

- Multiple nodes in a single process will share the same logging and metrics output.
- The nodes largely log via the static Serilog logger. The `EmbeddedKurrentDB` type has a `LoggerConfiguration` instance property which you can use to create a logger and assign to `Serilog.Log.Logger`.

## Trimming

This package ships with optional features which can be trimmed out at build time according to the configuration of your application by setting the `Value`s as follows:

```xml
<RuntimeHostConfigurationOption Include="KurrentDB.Plugins.AutoScavenge" Value="false" Trim="true" />
<RuntimeHostConfigurationOption Include="KurrentDB.Plugins.SecondaryIndexes" Value="true" Trim="true" />
```

SecondaryIndexing requires SchemaRegistry. See the sample for the full list. The values are all true by default.

## License

Kurrent License v1. See `LICENSE.md` in this package.
