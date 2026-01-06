# KurrentDB.Testing

A comprehensive testing toolkit for KurrentDB that provides modern testing infrastructure, test data generation, advanced assertions, and observability integration.

## Overview

KurrentDB.Testing is an opinionated testing framework designed to standardize and enhance the testing experience across the KurrentDB codebase. It provides:

- **Modern Testing Framework**: Built on TUnit with enhanced test execution and lifecycle management
- **Test Environment Management**: Automated configuration, dependency injection, and logging setup
- **Test Data Generation**: Powerful fake data generation using Bogus with custom DataSets
- **Advanced Assertions**: Deep object graph comparison with configurable equivalency testing
- **Observability**: Integrated logging (Serilog), OpenTelemetry, and test correlation via TUnit
- **Developer Experience**: Rich tooling support with Seq log aggregation and Aspire Dashboard

## ⚠️ CRITICAL: Required Setup

**Every test project that references KurrentDB.Testing MUST create a `TestEnvironmentWireUp.cs` file to bootstrap the test environment.**

### Minimal Required Version

This is the **absolute minimum** that every test project needs:

```csharp
using KurrentDB.Testing.TUnit;
using TUnit.Core.Executors;

// Register the toolkit configurator and executor at assembly level
[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace YourProject.Tests;

public class TestEnvironmentWireUp {
    [Before(Assembly)]
    public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Initialize(context.Assembly);

    [After(Assembly)]
    public static ValueTask AfterAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Reset(context.Assembly);
}
```

## Table of Contents

- [Getting Started](#getting-started)
- [Core Features](#core-features)
- [Test Environment Setup](#test-environment-setup)
- [Test Data Generation](#test-data-generation)
- [Advanced Assertions](#advanced-assertions)
- [Logging & Observability](#logging--observability)
- [HomeAutomation Sample](#homeautomation-sample)
- [Infrastructure](#infrastructure)
- [Contributing](#contributing)

## Getting Started

### 1. Add Package Reference

Add the KurrentDB.Testing project reference to your test project:

```xml
<ItemGroup>
  <ProjectReference Include="..\KurrentDB.Testing\KurrentDB.Testing.csproj" />
</ItemGroup>
```

### 2. Create Test Environment Wire-Up

**This step is MANDATORY.** See the [Required Setup](#️-critical-required-setup) section above for detailed instructions.

At minimum, create a `TestEnvironmentWireUp.cs` file with assembly-level hooks to initialize the test environment. Test-level hooks for logging are optional.

### 3. Write Your First Test

```csharp
public class MyFirstTests {
    [Test]
    public async ValueTask MyTest_ShouldPass() {
        // Arrange
        var expected = "Hello, World!";

        // Act
        var actual = "Hello, World!";

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }
}
```

## Core Features

### Test Environment Management

The `ToolkitTestEnvironment` class provides:

- **Configuration Management**: Loads `appsettings.json` and environment variables
- **Logging Setup**: Configures Serilog with multiple sinks (Console, Seq, Debug, Observable)
- **Service Collection**: Dependency injection support for test services
- **Test Correlation**: Integrates with TUnit's built-in test correlation - `TestUid` property is automatically added to all log events

### Test Execution

The `ToolkitTestExecutor` manages test execution lifecycle:

- Captures test-specific logs in an observable stream
- Provides scoped logging context per test
- Integrates with the test environment for consistent setup

### Test Context Extensions

Enhanced test context with utilities:

- `context.InjectItem<T>()`: Store data in test context
- `context.ExtractItem<T>()`: Retrieve stored data
- Logging extensions for structured test output

## Test Data Generation

### Using Bogus with TUnit

Bogus should be used as a TUnit ClassDataSource for test data generation:

```csharp
public class MyTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async Task GenerateRandomPerson() {
        var name = Faker.Name.FullName();
        var email = Faker.Internet.Email();

        await Assert.That(name).IsNotNull();
    }
}
```

This approach ensures:
- Faker instances are properly shared across tests
- Consistent test data generation patterns
- Better integration with TUnit's lifecycle management

## Advanced Assertions

### Object Graph Equivalency

The toolkit provides `ShouldBeEquivalentTo` for deep object comparison:

```csharp
[Test]
public async Task DeepComparison_ShouldSucceed() {
    var expected = new Order {
        Id = 1,
        Items = new List<OrderItem> {
            new() { ProductId = 100, Quantity = 2 },
            new() { ProductId = 101, Quantity = 1 }
        }
    };

    var actual = GetOrder();

    actual.ShouldBeEquivalentTo(expected, config => config
        .Excluding(x => x.CreatedAt)
        .WithStringComparison(StringComparison.OrdinalIgnoreCase)
        .WithNumericTolerance(0.01));
}
```

### Configuration Options

```csharp
config => config
    .Excluding(x => x.Property)              // Exclude specific properties
    .Excluding("Path.To.Property")           // Exclude by path string
    .Using<DateTime>((a, e) => a.Date == e.Date)  // Custom comparer
    .WithStringComparison(StringComparison.OrdinalIgnoreCase)
    .WithNumericTolerance(0.01)              // Tolerance for numeric comparisons
    .IgnoringCollectionOrder()               // Order-independent collection comparison
```

### Subset Assertions

```csharp
[Test]
public async Task SubsetComparison() {
    var subset = new[] { 1, 2, 3 };
    var collection = new[] { 1, 2, 3, 4, 5 };

    subset.ShouldBeSubsetOf(collection);
}
```

## Logging & Observability

### Test Correlation with TUnit

Test correlation is built into TUnit. The testing toolkit automatically adds a `TestUid` property to all log events, allowing you to correlate logs with specific tests.

All logs generated during test execution are automatically tagged with the test's unique identifier for easy filtering and debugging in Seq or other log aggregation tools.

```csharp
[Test]
public async Task MyTest(TestContext context) {
    // TestUid is automatically added to all logs
    Log.Information("Processing test");

    // All logs will be tagged with TestUid for correlation
}
```

### OpenTelemetry Integration

Add OTel metadata to test context:

```csharp
context.AddOtelServiceMetadata(new OtelServiceMetadata {
    ServiceName = "MyService",
    ServiceVersion = "1.0.0"
});
```

## HomeAutomation Sample

The HomeAutomation sample demonstrates a complete implementation of the testing toolkit for a smart home domain.

### Domain Model

- **SmartHome**: Contains rooms and devices
- **Room**: Physical spaces (Living Room, Kitchen, Bedroom, etc.)
- **Device**: Smart devices (Thermostat, SmartLight, MotionSensor, DoorLock, etc.)
- **Events**: Device state changes and readings

### DataSet Usage

```csharp
[Test]
public async Task GenerateSmartHome() {
    var faker = new Faker();

    // Generate a home with 5 rooms, 2 devices per room
    var home = faker.HomeAutomation().Home(rooms: 5, devicesPerRoom: 2);

    await Assert.That(home.Rooms.Count).IsEqualTo(5);
    await Assert.That(home.Devices.Count).IsEqualTo(10);
}
```

### Generating Specific Device Types

```csharp
[Test]
public async Task GenerateOnlyLightsAndSensors() {
    var faker = new Faker();
    var allowedTypes = new[] {
        DeviceType.SmartLight,
        DeviceType.MotionSensor
    };

    var home = faker.HomeAutomation().Home(deviceTypes: allowedTypes);

    foreach (var device in home.Devices) {
        await Assert.That(allowedTypes).Contains(device.DeviceType);
    }
}
```

### Generating Events

```csharp
[Test]
public async Task GenerateHomeEvents() {
    var faker = new Faker();
    var home = faker.HomeAutomation().Home();

    // Generate 50 events for this home's devices
    var events = faker.HomeAutomation().Events(home, count: 50);

    await Assert.That(events.Count).IsGreaterThanOrEqualTo(50);
}
```

### Event Correlation

The HomeAutomation DataSet includes smart event correlation:

```csharp
// When motion is detected, lights in the same room may turn on
var events = faker.HomeAutomation().Events(home, count: 10);

// Events are automatically correlated:
// 1. MotionDetected in Living Room
// 2. LightStateChanged (Living Room light turns on) - correlated 2-10 seconds later
```

## Infrastructure

The testing toolkit includes a docker-compose configuration that runs:

1. **Seq** (http://localhost:5341)
   - Centralized log aggregation
   - Rich querying and filtering
   - Test correlation by TestUid

2. **Aspire Dashboard** (http://localhost:18888)
   - OpenTelemetry visualization
   - Distributed tracing
   - Metrics and logs

Start the infrastructure:

```bash
docker-compose up -d
```

## Contributing

When adding new testing utilities:

1. Follow the established patterns (DataSets, Extensions, etc.)
2. Include XML documentation
3. Add tests demonstrating usage
4. Update this README with examples
