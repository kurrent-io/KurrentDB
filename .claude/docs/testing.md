# Testing Infrastructure (KurrentDB.Testing)

> Read this when: writing tests, creating test fixtures, or setting up a new test project.

## Overview

`KurrentDB.Testing` is a comprehensive toolkit for standardizing test infrastructure across KurrentDB. Use this for all new test projects.

## Required Setup

**Every test project MUST create `TestEnvironmentWireUp.cs`**:

```csharp
using KurrentDB.Testing.TUnit;
using TUnit.Core.Executors;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace YourProject.Tests;

public class TestEnvironmentWireUp {
    [Before(Assembly)]
    public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Initialize();

    [After(Assembly)]
    public static ValueTask AfterAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Reset();
}
```

## Advanced Assertions

Use `ShouldBeEquivalentTo` for deep object comparison:

```csharp
actual.ShouldBeEquivalentTo(expected, config => config
    .Excluding(x => x.CreatedAt)              // Exclude properties
    .Excluding("Path.To.Property")            // Exclude by path
    .Using<DateTime>((a, e) => a.Date == e.Date)  // Custom comparer
    .WithStringComparison(StringComparison.OrdinalIgnoreCase)
    .WithNumericTolerance(0.01)               // Numeric tolerance
    .IgnoringCollectionOrder()                // Order-independent collections
);
```

## Test Data Generation

Use Bogus as a TUnit ClassDataSource:

```csharp
public class MyTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerAssembly)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async Task GenerateTestData() {
        var name = Faker.Name.FullName();
        var email = Faker.Internet.Email();
    }
}
```

## Integration Testing

Use `KurrentContext` (preferred) or `ClusterVNodeTestContext` for full node integration tests. `KurrentContext` wraps the shared test infrastructure and supports running against both embedded and external servers:

```csharp
[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
public required KurrentContext KurrentContext { get; init; }

StreamsService.StreamsServiceClient StreamsV2Client => KurrentContext.StreamsV2Client;
EventStore.Client.Streams.Streams.StreamsClient StreamsClient => KurrentContext.StreamsClient;

[Test]
public async Task IntegrationTest() {
    // Use specific gRPC clients exposed by KurrentContext:
    // StreamsV2Client, StreamsClient, IndexesClient,
    // PersistentSubscriptionsClient, ConnectorsCommandServiceClient, etc.
}
```

Avoid building custom test fixtures when `KurrentContext` already handles lifecycle, configuration, and teardown.

## Test Infrastructure

**Docker Compose** (`src/KurrentDB.Testing/docker-compose.yml`):
- **Seq** (http://localhost:5341): Log aggregation with test correlation
- **Aspire Dashboard** (http://localhost:18888): OpenTelemetry visualization

Start with: `docker-compose up -d` in the KurrentDB.Testing directory

## File Locations

- `src/KurrentDB.Testing/` - Main toolkit project
- `src/KurrentDB.Testing/README.md` - Complete documentation (325 lines)
- `src/KurrentDB.Testing/Sample/HomeAutomation/` - Example DataSet implementation
- `src/KurrentDB.Testing.ClusterVNodeApp/` - Integration test harness

## Writing Integration Tests

1. Add `KurrentDB.Testing` project reference
2. Create `TestEnvironmentWireUp.cs` (required!)
3. Use `KurrentContext` or `ClusterVNodeTestContext` for full node tests
4. Use `ShouldBeEquivalentTo` for complex assertions
5. Use `BogusFaker` for test data generation

## Debugging Tips

1. **API v2 Services**: Set breakpoints in `ApiCallback.OnMessage` to see all message responses
2. **Enumerators**: Check `_channel.Reader` in enumerator implementations to see queued responses
3. **Protocol Buffers**: Use `message.ToString()` for debugging (formatted output)
4. **Test Correlation**: Search logs in Seq by `TestUid` property for test-specific logs
