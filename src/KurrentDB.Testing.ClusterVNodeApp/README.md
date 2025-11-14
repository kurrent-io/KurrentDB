# KurrentDB.Testing.ClusterVNodeApp

**The production-like KurrentDB test harness for integration tests.**

## Why Use This?

**Always use `ClusterVNodeApp` for integration tests.** Here's why:

### ✅ Production Fidelity
- **Same codebase** - Uses the exact same `ClusterVNode`, `ClusterVNodeHostedService`, and `ClusterVNodeStartup` as production
- **Same services** - All gRPC services, HTTP controllers, and plugins run identically
- **Same configuration** - Supports all production configuration options
- **Same lifecycle** - Startup, shutdown, and message bus behavior is identical

### ✅ Test Performance
- **No Docker overhead** - Starts in milliseconds, not seconds
- **In-memory by default** - Fast test execution without disk I/O
- **Configurable for persistence** - Can test with disk storage and restarts when needed
- **Dynamic ports** - Run multiple instances in parallel without conflicts

### ✅ Developer Experience
- **Simple setup** - One class, minimal configuration
- **Full DI access** - Inject and resolve any internal service
- **Easy debugging** - Step through production code directly
- **TUnit integration** - Shared fixtures with `ClassDataSource`

**The goal:** Provide a single-node production-like environment for testing - the only missing piece is a working dev certificate provider for macOS Sequoia to enable full auth/TLS testing.

## ClusterVNodeApp vs MiniNode

`ClusterVNodeApp` is similar to `MiniNode` but is much closer to a production deployment because it includes the `ClusterVNodeHostedService`.

## Architecture Overview

`ClusterVNodeApp` mirrors the production server's architecture:

```
Production (Program.cs)          →    ClusterVNodeApp (Test Harness)
├─ Configuration (YAML/ENV)      →    In-memory dictionary
├─ ClusterVNodeHostedService     →    ✓ Same
├─ ClusterVNode                  →    ✓ Same
├─ ClusterVNodeStartup           →    ✓ Same
├─ gRPC Services                 →    ✓ Same
├─ HTTP Controllers              →    ✓ Same
├─ Plugin System                 →    ✓ Same
├─ WebApplication Host           →    Slim WebApplication (optimized)
└─ Certificate Provider          →    Simplified (no dev cert support yet)
```

### Key Differences from Production

| Aspect            | Production Server              | ClusterVNodeApp                            |
|-------------------|--------------------------------|--------------------------------------------|
| **Configuration** | YAML files, ENV vars, CLI args | In-memory dictionary                       |
| **Database**      | Persistent on disk             | In-memory (MemDb)                          |
| **Security**      | Configurable TLS/Auth          | Always insecure                            |
| **Admin UI**      | Blazor UI enabled              | Disabled (for now)                         |
| **Port**          | Fixed (2113)                   | Dynamic (random port)                      |
| **Telemetry**     | Opt-in                         | Opt-out                                    |
| **Logging**       | File + Console                 | Seq + Console and configurable via Serilog |

## Components

### ClusterVNodeApp
The main test harness class that orchestrates the KurrentDB instance.

**Key Features:**
- Test-optimized default settings (in-memory, insecure, no UI)
- Configuration override mechanism
- Service injection hook for custom test setup
- Automatic gRPC client address discovery (supports random/dynamic ports)
- Built-in gzip compression for all gRPC clients
- Async lifecycle (`Start()`, `DisposeAsync()`)

## Default Configuration

`ClusterVNodeApp` applies these test-optimized defaults:

```csharp
{
    "KurrentDB:Application:TelemetryOptout": "true",
    "KurrentDB:Application:Insecure": "true",
    "KurrentDB:Database:MemDb": "true",
    "KurrentDB:Interface:DisableAdminUi": "true",
    "KurrentDB:DevMode:Dev": "true",
    "KurrentDB:Logging:LogLevel": "Default",
    "KurrentDB:Logging:DisableLogFile": "true"
}
```

**Why these defaults?**
- **MemDb**: No disk I/O, fast startup, isolated tests
- **Insecure**: No TLS/auth overhead for faster test execution
- **DisableAdminUi**: Reduces startup time, unnecessary for tests
- **Dev Mode**: Simplified certificate handling
- **DisableLogFile**: Console logging only

## Usage

### Basic Usage

```csharp
using KurrentDB.Testing;

// Create and start the test server
await using var server = new ClusterVNodeApp();
await server.Start();

// Server is now ready to accept requests
// Access services via DI container
var options = server.ServerOptions;
var services = server.Services;

// Your test code here...

// Server stops automatically when disposed
```

### Configuration Overrides

```csharp
var overrides = new Dictionary<string, object?> {
    ["KurrentDB:Interface:NodePort"] = 2113,  // Fixed port instead of random
    ["KurrentDB:Cluster:ClusterSize"] = 3,    // Cluster mode
    ["KurrentDB:Database:MemDb"] = false      // Use disk storage
};

await using var server = new ClusterVNodeApp(overrides: overrides);
await server.Start();
```

### Custom Service Configuration

```csharp
await using var server = new ClusterVNodeApp(
    configureServices: (options, services) => {
        // Add custom test services
        services.AddSingleton<IMyTestService, MyTestService>();

        // Replace default implementations
        services.Replace(ServiceDescriptor.Singleton<IFoo, MockFoo>());

        // Configure existing services
        services.Configure<MyOptions>(o => o.TestMode = true);
    }
);

await server.Start();
```

### gRPC Client Setup

```csharp
await using var server = new ClusterVNodeApp();
await server.Start();

// Get the server's actual address (dynamic port)
var serverAddress = server.Services.GetServerLocalAddress();

// Create gRPC client pointing to test server
var channel = GrpcChannel.ForAddress(serverAddress);
var client = new Streams.StreamsClient(channel);

// Or use DI-configured clients (already set up via EnableGrpcClientsAddressDiscovery)
var grpcClient = server.Services.GetRequiredService<Streams.StreamsClient>();
```

### Accessing Internal Services

```csharp
await using var server = new ClusterVNodeApp();
await server.Start();

// Access any registered service
var mainBus = server.Services.GetRequiredService<IPublisher>();
var mainQueue = server.Services.GetRequiredService<ISubscriber>();
var authProvider = server.Services.GetRequiredService<IAuthenticationProvider>();

// Access server options
var dbPath = server.ServerOptions.Database.Db;
var nodePort = server.ServerOptions.Interface.NodePort;
```

## Integration with Tests

### TUnit Example (Recommended)

TUnit supports shared fixtures via `ClassDataSource`. This is the recommended pattern for ClusterVNodeApp:

```csharp
// 1. Create a test context/fixture that wraps ClusterVNodeApp
public sealed class ClusterVNodeTestContext : IAsyncInitializer, IAsyncDisposable {
    ClusterVNodeApp _server = null!;

    public ClusterVNodeTestContext() {
        _server = new ClusterVNodeApp(ConfigureServices);
    }

    static void ConfigureServices(ClusterVNodeOptions options, IServiceCollection services) {
        // Add your test-specific services
        services.AddGrpcClient<StreamsServiceClient>();
    }

    public IServiceProvider Services => _server.Services;
    public StreamsServiceClient StreamsClient { get; private set; } = null!;

    public async Task InitializeAsync() {
        await _server.Start();
        StreamsClient = Services.GetRequiredService<StreamsServiceClient>();
    }

    public async ValueTask DisposeAsync() =>
        await _server.DisposeAsync();
}

// 2. Use the fixture in your tests
public class MyIntegrationTests {
    // Shared fixture - created once per test session
    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
    public required ClusterVNodeTestContext Fixture { get; init; }

    [Test]
    public async Task Should_Append_Events() {
        var client = Fixture.StreamsClient;

        // Your test code...
    }

    [Test]
    public async Task Should_Read_Events() {
        var systemClient = Fixture.Services.GetRequiredService<ISystemClient>();

        // Your test code...
    }
}
```

> **Note:** All new tests should use TUnit. The fixture pattern above is the recommended approach for integration tests.

## Known Limitations

### 1. Authentication/TLS (macOS Sequoia Issue)
The server runs in **insecure mode by default** because dev certificate generation is broken on macOS Sequoia. Until this is resolved, certificate-based authentication and TLS are disabled.

**Impact:** Authentication and authorization testing requires manual certificate configuration.

**Workaround:** Provide certificates manually via configuration:
```csharp
var overrides = new Dictionary<string, object?> {
    ["KurrentDB:Application:Insecure"] = false,
    ["KurrentDB:Certificates:CertificateFile"] = "/path/to/cert.pfx",
    ["KurrentDB:Certificates:CertificatePassword"] = "password"
};
```

### 2. Admin UI Disabled
The Admin UI is disabled by default for faster startup. Enable it for debugging:

```csharp
var overrides = new Dictionary<string, object?> {
    ["KurrentDB:Interface:DisableAdminUi"] = false
};
```

## Comparison: ClusterVNodeApp vs Program.cs

Both follow the **same core initialization path**. The differences are minimal and only related to how configuration is loaded and default settings:

| Aspect                   | Program.cs (Production)                             | ClusterVNodeApp (Test)  |
|--------------------------|-----------------------------------------------------|-------------------------|
| **Initialization**       | `ClusterVNodeHostedService` → `ClusterVNodeStartup` | ✅ Same                  |
| **Service Registration** | `ClusterVNodeStartup.ConfigureServices()`           | ✅ Same                  |
| **Middleware Pipeline**  | `ClusterVNodeStartup.Configure()`                   | ✅ Same                  |
| **ClusterVNode**         | Production instance with all subsystems             | ✅ Same                  |
| **Configuration Source** | YAML/ENV/CLI                                        | ✅ +In-memory dictionary |
| **Web Host**             | Full `WebApplication`                               | Slim `WebApplication`   |
| **Port**                 | Fixed (2113)                                        | Dynamic (random)        |
| **Defaults**             | Production settings                                 | Test-optimized settings |

Going forward, it should be possible to **converge the two code paths further** by using the MS Test host that calls `Program.cs` directly.

## See Also

- [KurrentDB.Testing](../KurrentDB.Testing/) - Shared testing infrastructure
- [ClusterVNode](../KurrentDB.Core/ClusterVNode.cs) - The core database engine
- [ClusterVNodeStartup](../KurrentDB.Core/ClusterVNodeStartup.cs) - Service configuration
- [Program.cs](../KurrentDB/Program.cs) - Production entry point
