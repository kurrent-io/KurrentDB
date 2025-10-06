# KurrentDB.Testing

A comprehensive testing toolkit for KurrentDB that provides modern testing infrastructure, test data generation, advanced assertions, and observability integration.

## Overview

KurrentDB.Testing is an opinionated testing framework designed to standardize and enhance the testing experience across the KurrentDB codebase. It provides:

- **Modern Testing Framework**: Built on TUnit with enhanced test execution and lifecycle management
- **Test Environment Management**: Automated configuration, dependency injection, and logging setup
- **Test Data Generation**: Powerful fake data generation using Bogus with custom DataSets
- **Advanced Assertions**: Deep object graph comparison with configurable equivalency testing
- **Observability**: Integrated logging (Serilog), OpenTelemetry, and test correlation
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

### Enhanced Version (Optional)

You can optionally add test-level hooks for logging test lifecycle events:

```csharp
using KurrentDB.Testing.TUnit;
using Serilog;
using TUnit.Core.Executors;

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

    // Optional: Log test lifecycle events
    [BeforeEvery(Test)]
    public static void BeforeEveryTest(TestContext context) {
        var testUid = context.TestUid();
        Log.ForContext(nameof(TestUid), testUid).Verbose(
            "{TestClass} {TestName} {Status} TestUid: {TestUid}",
            context.TestDetails.ClassType.Name,
            context.TestDetails.TestName,
            TestState.NotStarted,
            testUid
        );
    }

    [AfterEvery(Test)]
    public static void AfterEveryTest(TestContext context) {
        var testUid = context.TestUid();
        var elapsed = context.Result?.Duration ?? TimeSpan.Zero;
        Log.ForContext(nameof(TestUid), testUid).Verbose(
            "{TestClass} {TestName} {Status} in {Elapsed}",
            context.TestDetails.ClassType.Name,
            context.TestDetails.TestName,
            context.Result!.State,
            elapsed.Humanize(2)
        );
    }
}
```

**See [KurrentDB.Api.V2.Tests/TestEnvironmentWireUp.cs](../KurrentDB.Api.V2.Tests/TestEnvironmentWireUp.cs) for a real example.**

## Table of Contents

- [Getting Started](#getting-started)
- [Core Features](#core-features)
- [Test Environment Setup](#test-environment-setup)
- [Test Data Generation](#test-data-generation)
- [Advanced Assertions](#advanced-assertions)
- [Logging & Observability](#logging--observability)
- [HomeAutomation Sample](#homeautomation-sample)
- [Infrastructure](#infrastructure)
- [Best Practices](#best-practices)
- [API Reference](#api-reference)

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
- **Test Correlation**: Automatic TestUid generation for log correlation

### Test Execution

The `ToolkitTestExecutor` manages test execution lifecycle:

- Captures test-specific logs in an observable stream
- Provides scoped logging context per test
- Integrates with the test environment for consistent setup

### Test Context Extensions

Enhanced test context with utilities:

- `context.TestUid()`: Get unique identifier for test correlation
- `context.InjectItem<T>()`: Store data in test context
- `context.ExtractItem<T>()`: Retrieve stored data
- Logging extensions for structured test output

## Test Environment Setup

### Configuration

Place an `appsettings.json` in your test project:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Verbose",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  }
}
```

### Logging

Serilog is configured with multiple enrichers:

- **Demystifier**: Clean stack traces
- **Environment**: Machine and user info
- **Process**: Process ID and name
- **Thread**: Thread information

Output template:
```
[{Timestamp:HH:mm:ss.fff} {Level:u4} <{ThreadId:000}> {SourceContext}] {Message:lj}{NewLine}{Exception}
```

## Test Data Generation

### Using Bogus Directly

```csharp
[Test]
public async Task GenerateRandomPerson() {
    var faker = new Faker();
    var name = faker.Name.FullName();
    var email = faker.Internet.Email();

    await Assert.That(name).IsNotNull();
}
```

### Creating Custom DataSets

DataSets provide reusable, composable fake data generators:

```csharp
public class ProductDataSet : DataSet {
    public ProductDataSet(string locale = "en") : base(locale) {
        Faker = new Faker(locale);
    }

    Faker Faker { get; }

    public Product Product() {
        return new Product {
            Id = Faker.Random.Guid(),
            Name = Faker.Commerce.ProductName(),
            Price = Faker.Finance.Amount(1, 1000),
            Description = Faker.Commerce.ProductDescription()
        };
    }

    public List<Product> Products(int count = 5) {
        return Enumerable.Range(0, count)
            .Select(_ => Product())
            .ToList();
    }
}
```

### Using Extension Methods

```csharp
public static class ProductDataSetExtensions {
    public static ProductDataSet Products(this Faker faker) =>
        new ProductDataSet(faker.Locale);
}

// Usage
var product = new Faker().Products().Product();
```

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

### Test Correlation with TestUid

Every test gets a unique 12-character hexadecimal identifier:

```csharp
[Test]
public async Task MyTest(TestContext context) {
    var testUid = context.TestUid();

    Log.ForContext(nameof(TestUid), testUid)
       .Information("Processing test with ID: {TestUid}", testUid);

    // All logs will be tagged with this TestUid for correlation
}
```

### Capturing Test Logs

```csharp
[Test]
public async Task CaptureTestLogs(TestContext context) {
    var logs = new List<LogEvent>();

    await ToolkitTestEnvironment.CaptureTestLogs(context, logEvent => {
        logs.Add(logEvent);
    }, async () => {
        Log.Information("This log will be captured");
        await DoSomething();
    });

    await Assert.That(logs).IsNotEmpty();
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

### Creating Your Own DataSet

Use HomeAutomation as a template:

1. **Create a DataSet class**:
   ```csharp
   public class YourDataSet : DataSet {
       public YourDataSet(string locale = "en") : base(locale) {
           Faker = new Faker(locale);
       }

       Faker Faker { get; }

       // Add generation methods
   }
   ```

2. **Create Faker classes** for complex types:
   ```csharp
   public class YourEntityFaker : Faker<YourEntity> {
       public YourEntityFaker() {
           RuleFor(x => x.Id, f => f.Random.Guid());
           RuleFor(x => x.Name, f => f.Company.CompanyName());
       }
   }
   ```

3. **Add extension methods** for convenient access:
   ```csharp
   public static class YourDataSetExtensions {
       public static YourDataSet YourDomain(this Faker faker) =>
           new YourDataSet(faker.Locale);
   }
   ```

## Infrastructure

### Docker Compose

Start the testing infrastructure:

```bash
docker-compose up -d
```

This starts:

1. **Seq** (http://localhost:5341)
   - Centralized log aggregation
   - Rich querying and filtering
   - Test correlation by TestUid

2. **Aspire Dashboard** (http://localhost:18888)
   - OpenTelemetry visualization
   - Distributed tracing
   - Metrics and logs

### Services Configuration

```yaml
services:
  seq:
    image: datalust/seq:latest
    ports:
      - "5341:80"
    environment:
      - ACCEPT_EULA=Y
      - SEQ_FIRSTRUN_NOAUTHENTICATION=True

  aspire-dashboard:
    image: mcr.microsoft.com/dotnet/aspire-dashboard:latest
    ports:
      - "18888:18888"  # Dashboard UI
      - "4317:18889"   # OTLP ingestion
    environment:
      - ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true
```

### Viewing Test Logs in Seq

1. Navigate to http://localhost:5341
2. Filter by TestUid to see all logs for a specific test:
   ```
   TestUid = "A1B2C3D4E5F6"
   ```
3. Use structured queries:
   ```
   TestClass = "HomeAutomationTests" and Level = "Error"
   ```

## Best Practices

### 1. Always Create TestEnvironmentWireUp (MANDATORY)

**Every test project MUST have a `TestEnvironmentWireUp.cs` file** with assembly-level hooks to bootstrap the test environment. See the [Required Setup](#️-critical-required-setup) section for the minimal required implementation. Test-level hooks for logging are optional enhancements.

### 2. Use TestUid for Correlation

Always include TestUid in log contexts for debugging:

```csharp
Log.ForContext(nameof(TestUid), context.TestUid())
   .Information("Processing {Entity}", entity);
```

### 3. Organize DataSets by Domain

Create domain-specific DataSets rather than generic "TestData" classes:

```csharp
// Good
var order = faker.Orders().Order();
var customer = faker.Customers().Customer();

// Avoid
var order = TestDataGenerator.GenerateOrder();
```

### 4. Use Equivalency Testing for Complex Objects

Prefer `ShouldBeEquivalentTo` over manual property comparisons:

```csharp
// Good
actual.ShouldBeEquivalentTo(expected, config => config
    .Excluding(x => x.CreatedAt));

// Avoid
Assert.That(actual.Id).IsEqualTo(expected.Id);
Assert.That(actual.Name).IsEqualTo(expected.Name);
// ... 20 more properties
```

### 5. Configure Logging Appropriately

Use appropriate log levels in tests:

```csharp
Log.Verbose("Detailed diagnostic info");
Log.Debug("Debug information");
Log.Information("Important milestones");
Log.Warning("Unexpected but handled");
Log.Error("Failures and exceptions");
```

### 6. Clean Up Resources

Use proper disposal patterns:

```csharp
[Test]
public async Task TestWithResources() {
    await using var resource = CreateResource();

    // Test code
}
```

## API Reference

### ToolkitTestEnvironment

```csharp
public static class ToolkitTestEnvironment {
    // Initialize the test environment for an assembly
    public static ValueTask Initialize(Assembly assembly);

    // Reset the test environment
    public static ValueTask Reset(Assembly assembly);

    // Capture logs during test execution
    public static Task CaptureTestLogs(
        TestContext context,
        Action<LogEvent> onLogEvent,
        Func<Task> testAction);

    // Access configuration
    public static IConfiguration Configuration { get; }

    // Access captured log events
    public static IObservable<LogEvent> LogEvents { get; }
}
```

### TestContext Extensions

```csharp
public static class TestContextExtensions {
    // Get unique test identifier
    public static TestUid TestUid(this TestContext context);

    // Assign custom test identifier
    public static void AssignTestUid(this TestContext context, TestUid uid);

    // Store and retrieve data
    public static void InjectItem<T>(this TestContext context, T item);
    public static T ExtractItem<T>(this TestContext context);
    public static bool TryExtractItem<T>(this TestContext context, out T? item);
}
```

### TestUid

```csharp
public readonly record struct TestUid {
    // Generate new unique identifier
    public static TestUid New();

    // Parse from string
    public static TestUid Parse(string input);
    public static bool TryParse(string input, out TestUid uid);

    // Empty instance
    public static readonly TestUid Empty;

    // 12-character hex value
    public string Value { get; }
}
```

### Shouldly Extensions

```csharp
public static class ShouldlyObjectGraphTestExtensions {
    // Deep equivalency comparison
    public static void ShouldBeEquivalentTo(
        this object actual,
        object expected,
        Action<EquivalencyConfiguration>? configure = null,
        string? customMessage = null);

    // Subset comparison
    public static void ShouldBeSubsetOf<T>(
        this IEnumerable<T> subset,
        IEnumerable<T> collection,
        Action<EquivalencyConfiguration>? configure = null,
        string? customMessage = null);
}

public class EquivalencyConfiguration {
    // Exclude properties
    public EquivalencyConfiguration Excluding<T>(Expression<Func<T, object>> property);
    public EquivalencyConfiguration Excluding(string propertyPath);

    // Custom comparers
    public EquivalencyConfiguration Using<T>(Func<T, T, bool> comparer);

    // String comparison
    public EquivalencyConfiguration WithStringComparison(StringComparison comparison);

    // Numeric tolerance
    public EquivalencyConfiguration WithNumericTolerance(double tolerance);

    // Collection ordering
    public EquivalencyConfiguration IgnoringCollectionOrder();
}
```

### DataSet (Bogus)

```csharp
// Inherit from Bogus.DataSet
public class YourDataSet : DataSet {
    public YourDataSet(string locale = "en") : base(locale);

    // Add your generation methods
    public YourEntity Entity() { ... }
    public List<YourEntity> Entities(int count) { ... }
}
```

### Extension Utilities

```csharp
public static class WithExtensions {
    // Fluent property setters
    public static T With<T>(this T obj, Action<T> action);
    public static T With<T, TValue>(this T obj, Action<T> action, TValue value);
}
```

## Contributing

When adding new testing utilities:

1. Follow the established patterns (DataSets, Extensions, etc.)
2. Include XML documentation
3. Add tests demonstrating usage
4. Update this README with examples

## License

Kurrent License v1 (see LICENSE.md)
