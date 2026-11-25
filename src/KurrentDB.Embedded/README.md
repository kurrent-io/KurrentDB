# KurrentDB.Embedded

Runs a single-node KurrentDB instance inside your own process, reachable over a UNIX domain socket located in the database directory.

The regular KurrentDB .NET gRPC client can be used to access the socket (see the sample).

No ports are listened on by default.

A connection over the UNIX domain socket is automatically authenticated as the system account without need for credentials, and has full administrative access to the database.
The socket and data both need suitable file system permissions. On Linux and macOS the data directory is restricted to its owner automatically: it is created that way, or narrowed if it already exists. On Windows the permissions are left as they are, so choose a data directory that only the current user can access, such as one under `%LOCALAPPDATA%`.

## Sample

https://github.com/kurrent-io/KurrentDB/tree/v26.3.0-alpha.6/src/KurrentDB.Embedded.Sample

## Limitations

- Running more than one node at a time in a single process is not yet supported: they would share the same logging and metrics output, and a fatal error in any of them would be reported to the most recently started one. Running them one after another is fine.
- The nodes largely log via the static Serilog logger. The `EmbeddedKurrentDB` type has a `LoggerConfiguration` instance property which you can use to create a logger and assign to `Serilog.Log.Logger`.
- Some of this package's dependencies bring in `System.Linq.Async`, whose extension methods clash with the async LINQ methods built into .NET 10. If calls such as `ToListAsync()` on an `IAsyncEnumerable<T>` fail to compile as ambiguous, reference the package directly and exclude its compile assets. It is still needed at runtime.

  ```xml
  <PackageReference Include="System.Linq.Async" Version="6.0.3">
    <ExcludeAssets>compile</ExcludeAssets>
  </PackageReference>
  ```

## Trimming

This package ships with optional features which can be trimmed out at build time according to the configuration of your application by setting the `Value`s as follows:

```xml
<RuntimeHostConfigurationOption Include="KurrentDB.Plugins.AutoScavenge" Value="false" Trim="true" />
<RuntimeHostConfigurationOption Include="KurrentDB.Plugins.SecondaryIndexes" Value="true" Trim="true" />
```

SecondaryIndexing requires SchemaRegistry. See the sample for the full list. The values are all true by default.

## License

Kurrent License v1. See `LICENSE.md` in this package.
