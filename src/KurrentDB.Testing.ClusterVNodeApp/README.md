# KurrentDB.Testing.ClusterVNodeApp

A production-like KurrentDB test harness designed for integration tests that need a real database instance **without using Docker**.

## Purpose

This library provides `ClusterVNodeApp`, a testing infrastructure that creates a fully functional KurrentDB instance that's **as close to production as possible**, but optimized for test scenarios:

- ✅ Uses the same `ClusterVNode` and `ClusterVNodeHostedService` as the production server
- ✅ Supports the same configuration options and plugins
- ✅ Runs the same gRPC services and HTTP endpoints
- ✅ Uses the same startup/shutdown lifecycle
- ❌ No Docker required
- ❌ No disk I/O (runs in-memory by default)
- ❌ No authentication/TLS overhead (insecure mode)

**Use this when:** You need integration tests that exercise the full KurrentDB stack (gRPC, projections, persistent subscriptions, etc.) without the overhead of Docker containers.

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

### Custom Readiness Timeout

```csharp
await using var server = new ClusterVNodeApp();

// Wait up to 30 seconds for node to be ready
await server.Start(readinessTimeout: TimeSpan.FromSeconds(30));
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

### xUnit Example

```csharp
public class MyIntegrationTests : IAsyncLifetime {
    ClusterVNodeApp _server = null!;

    public async Task InitializeAsync() {
        _server = new ClusterVNodeApp();
        await _server.Start();
    }

    public async Task DisposeAsync() {
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Should_Append_Events() {
        var client = _server.Services.GetRequiredService<Streams.StreamsClient>();

        // Your test code...
    }
}
```

### TUnit Example (with Fixture)

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
    // Shared fixture - created once per test class (or assembly)
    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerClass)]
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

### NUnit Example

```csharp
[TestFixture]
public class MyIntegrationTests {
    ClusterVNodeApp _server = null!;

    [OneTimeSetUp]
    public async Task Setup() {
        _server = new ClusterVNodeApp();
        await _server.Start();
    }

    [OneTimeTearDown]
    public async Task Cleanup() {
        await _server.DisposeAsync();
    }

    [Test]
    public async Task Should_Append_Events() {
        var client = _server.Services.GetRequiredService<Streams.StreamsClient>();

        // Your test code...
    }
}
```

## Known Limitations

### 1. Development Certificate Authentication (Disabled)
Dev certificate-based authentication is currently **disabled** due to issues on macOS Sequoia. The `ConfigureCertificateProvider` method always returns `OptionsCertificateProvider` instead of `DevCertificateProvider`.

**Impact:** Tests requiring certificate-based authentication must provide their own certificates via configuration overrides.

**Workaround:**
```csharp
var overrides = new Dictionary<string, object?> {
    ["KurrentDB:Certificates:CertificateFile"] = "/path/to/cert.pfx",
    ["KurrentDB:Certificates:CertificatePassword"] = "password"
};

await using var server = new ClusterVNodeApp(overrides: overrides);
```

### 2. No UI Access
The Admin UI is disabled by default. If you need UI for debugging, override the setting:

```csharp
var overrides = new Dictionary<string, object?> {
    ["KurrentDB:Interface:DisableAdminUi"] = false
};
```

### 3. Dynamic Port Assignment
The server listens on a **random port** (0) by default. Always retrieve the actual address:

```csharp
var actualAddress = server.Services.GetServerLocalAddress();
Console.WriteLine($"Server listening on: {actualAddress}");
```

## Comparison: ClusterVNodeApp vs Program.cs

### Startup Flow

**Production (Program.cs):**
```
Configuration Loading → Option Validation → Certificate Setup →
ClusterVNodeHostedService Creation → WebApplication Build →
Kestrel Configuration → Service Registration → App.Run()
```

**ClusterVNodeApp:**
```
In-Memory Config → Option Creation → ClusterVNodeHostedService Creation →
Slim WebApplication Build → Service Registration → Custom Config Hook →
Readiness Probe Setup → Web.Build() → Start() → Wait for Ready
```

### Service Registration

Both use the same service registration pipeline:
1. `ClusterVNodeStartup.ConfigureServices()` - Core services
2. Plugin service registration
3. gRPC service registration
4. Custom configuration hook (ClusterVNodeApp only)

### Key Architectural Similarities

- ✅ Same `ClusterVNode` instance
- ✅ Same `ClusterVNodeHostedService` lifecycle
- ✅ Same plugin loading mechanism
- ✅ Same gRPC service implementations
- ✅ Same HTTP controller routing
- ✅ Same authentication/authorization providers (when configured)
- ✅ Same message bus and queuing infrastructure

### Key Architectural Differences

- ❌ Configuration source (files vs in-memory)
- ❌ Web host (full vs slim)
- ❌ Certificate provider (production vs test)
- ❌ Default settings (production-ready vs test-optimized)
- ❌ Readiness detection (external health check vs internal probe)

## When to Use This vs Docker

| Scenario                     | Use ClusterVNodeApp | Use Docker |
|------------------------------|---------------------|------------|
| Unit/integration tests       | ✅ Yes               | ❌ No       |
| Fast test execution          | ✅ Yes               | ❌ No       |
| CI/CD pipelines              | ✅ Yes               | ⚠️ Maybe   |
| Multi-node cluster testing   | ❌ No                | ✅ Yes      |
| Production-like environment  | ⚠️ Maybe            | ✅ Yes      |
| Testing with persistent data | ❌ No                | ✅ Yes      |
| Testing across restarts      | ❌ No                | ✅ Yes      |
| Local development            | ✅ Yes               | ✅ Yes      |

## See Also

- [KurrentDB.Testing](../KurrentDB.Testing/) - Shared testing infrastructure
- [ClusterVNode](../KurrentDB.Core/ClusterVNode.cs) - The core database engine
- [ClusterVNodeStartup](../KurrentDB.Core/ClusterVNodeStartup.cs) - Service configuration
- [Program.cs](../KurrentDB/Program.cs) - Production entry point
