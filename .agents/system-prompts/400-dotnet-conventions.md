<!-- <dotnet-conventions> -->

# Code Conventions

> Conventions that `.editorconfig` cannot enforce or only marks as suggestion.

## Style

- Braces: K&R (opening brace on same line)
- Fields: `_camelCase`
- Variables: `var` everywhere
- No `goto` — use `bool` flags with `break`, early `return`, or extract to a method

### Regions

Regions are for files with multiple logical sections — don't add them when a single region would wrap
all or most of the class content. Never use `// ---` style separators — use regions instead.

```csharp
#region ->> {Name} <<-
// Content
#endregion // {Name}
```

TRIPWIRE: About to add a `#region`. Does the class have at least two distinct groups that benefit from
separation? If the region would wrap all or nearly all of the class content, skip it.

### Code comments

* Err on the side of over-commenting code when the reasoning is not obvious. Comments should explain **WHY** code is written a particular way; the **WHY** is the most important part.
* Do comment non-obvious implementation details: concurrency hazards, lifecycle constraints, compatibility requirements, platform quirks, upstream workarounds, and intentional deviations from the obvious helper or API.
* When parsing strings, logs, command output, protocol payloads, or other loosely structured data, include a comment with an example of the raw format being parsed. Show edge cases, escaping rules, delimiters, optional fields, or malformed-but-observed inputs when they affect the parser.
* When code follows an external standard, protocol, or ecosystem convention, include valid links to the relevant source material so future readers can verify the rule and understand why the code follows it.
* Do not add comments that simply narrate clear code, such as "set the timeout" immediately before assigning a timeout.
* Keep workaround comments close to the workaround. Include an issue link when the workaround is tied to an upstream bug, and describe the condition for removing it when that is known.

Good comments explain the constraint or tradeoff:

```csharp
// Read both streams concurrently to avoid deadlock when a pipe buffer fills.
var stdoutTask = process.StandardOutput.ReadToEndAsync();
var stderrTask = process.StandardError.ReadToEndAsync();
```

```csharp
// Endpoint adoption runs on the command path, so fail quickly when stale metadata
// points at a dead or reused port.
var timeout = TimeSpan.FromSeconds(2);
```

```csharp
// The temporary config is disposed when this method returns. That is intentional:
// only `dotnet new install` consumes the config; later template creation uses the
// already-installed template hive and ambient NuGet configuration.
using var temporaryConfig = await TemporaryNuGetConfig.CreateAsync(mappings);
```

```csharp
// Workaround for an upstream library bug on Windows where URI SANs are formatted
// differently than the verifier expects. Cryptographic verification still runs;
// only the identity checks are performed manually from the certificate extensions.
var result = await VerifyWithManualIdentityFallbackAsync(bundle, cancellationToken);
```

```csharp
// Output sensitive message content for GenAI.
// A convention for libraries that output GenAI telemetry is to use
// `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`.
// See:
// - https://opentelemetry.io/blog/2024/otel-generative-ai/
// - https://github.com/search?q=org%3Aopen-telemetry+OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT&type=code
context.EnvironmentVariables[KnownOtelConfigNames.InstrumentationGenAiCaptureMessageContent] = "true";
```

```csharp
// If we have multiple endpoints for the same scheme, differentiate them by appending a number.
// Start numbering with the second endpoint so the first stays just http/https, which preserves
// the same behavior as "dotnet run". Only do this in Run mode because, in Publish mode, those
// extra endpoints with generic names would not be easily usable.
var endpointName = bindingAddress.Scheme;
if (endpointCountByScheme[bindingAddress.Scheme] > 1)
{
    endpointName += endpointCountByScheme[bindingAddress.Scheme];
}
```

```csharp
// The implementation here is less than ideal, but we don't have a clean way of building resource
// types that change their behavior based on context. In this case, publish mode needs the resource
// to behave like a ContainerResource instead of a ProjectResource, so we remove the ProjectResource
// from the application model and add a new ContainerResource in its place.
//
// There are still dangling references to the original ProjectResource in the application model, but
// in publish mode it won't be used. This is a limitation of the current design.
builder.ApplicationBuilder.Resources.Remove(builder.Resource);
```

Parsing comments should show the raw shape and important edge cases:

```csharp
// Parse resource log lines emitted as:
//   [2026-05-10T18:34:22.123Z] frontend stdout: Now listening on: http://localhost:5221
// The message can contain additional ':' characters, so split only on the first
// " stdout: " or " stderr: " delimiter after the resource name.
var match = s_logLineRegex.Match(line);
```

```csharp
// The endpoint metadata sidecar uses the DevTools /json/version shape:
//   { "webSocketDebuggerUrl": "ws://127.0.0.1:50981/devtools/browser/<id>" }
// Older Chromium builds can omit the property while the browser is still starting;
// treat that as a retryable probe failure rather than invalid metadata.
var endpoint = payload.WebSocketDebuggerUrl;
```

Avoid comments that restate the code:

```csharp
// Set the timeout to two seconds.
var timeout = TimeSpan.FromSeconds(2);
```

## Naming

- Namespaces: feature-based, file-scoped (`Kurrent.Client.Streams`, not `Kurrent.Client.Modules.Streams`)
- Use meaningful, domain-specific names in public code — no abbreviations unless universally understood (`id`, `ct`, `dto`)
- Use abbreviations in internal code since they cost less tokens and are easier to type (`guid`, `cfg`, `svc`)

## Structure

- File-scoped namespaces everywhere (single `namespace X;` at the top)
- One primary type per file, file name matches type name
- Partial class files mostly follow: `{Module}Client.{Operation}.cs`, `{Module}Client.{Operation}.Models.cs`,
  `.Mappers.cs`, `.Extensions.cs`, `.Models.Builders.cs`
- Use `record` for immutable data, `readonly record struct` for small value types
- Prefer primary constructors for DI services and simple types:
  `public sealed class OrderService(IOrderRepository repo, ILogger<OrderService> logger)`
- When a type as many dependencies, consider creating options classes.

## Async

- `ConfigureAwait(false)` on ALL awaits in library code (`src/`)
- `ValueTask` for hot paths that often complete synchronously (caches, short-circuits)
- `Task` for real I/O that always goes async — simpler, no footguns
- `ValueTask` rules: never await twice, never `.Result` before completion
- Always propagate `CancellationToken` — every async method gets one as the last parameter
- Never block on async: no `.Result`, no `.GetAwaiter().GetResult()` — async all the way

```csharp
// BAD: blocking on async — deadlock risk
var order = GetOrderAsync(id).Result;

// GOOD: async all the way
var order = await GetOrderAsync(id, cancellationToken).ConfigureAwait(false);

// BAD: async in LINQ Select — hidden Task-per-item allocation
var results = orders.Select(async o => await ProcessAsync(o)).ToList();

// GOOD: IAsyncEnumerable for streaming
await foreach (var result in ProcessOrdersAsync(orders, ct)) { }

// GOOD: Task.WhenAll for parallel batch
var results = await Task.WhenAll(orders.Select(o => ProcessAsync(o, ct)));
```

TRIPWIRE: About to write `.Result` or `.GetAwaiter().GetResult()`? Async all the way.
About to write an async method without `CancellationToken`? Add it as the last parameter.

## Error Handling

- Public operations return `ValueTask<T>` and throw typed exceptions on failure
- Never add error handling for scenarios that cannot occur
- Don't catch `Exception` or `OperationCanceledException` unless you have a specific recovery strategy
- Don't use exceptions for flow control — use return values, discriminated unions, or `bool Try*(out T result)`
- Let unrecoverable exceptions propagate — the caller decides

## Type Mapping

- Use `MessageTypeMapper` registration, not reflection

## Public API Surface

This is a published SDK. Do not rename, remove, or change the signature of any `public` member without explicit
user approval. New public API surface requires discussion.

## Multi-Target Compatibility

This project targets net10 only. Always use the latest APIs available in net10. The exception are source
generators, that require netstandard2.0.

## Nullable Reference Types

NRTs are enabled project-wide (`<Nullable>enable</Nullable>` in `Directory.Build.props`).

- Declare variables non-nullable, and check for `null` at entry points.
- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.
- Never use `ArgumentNullException.ThrowIfNull()` on non-nullable parameters — it's dead code that looks like
  good practice.

TRIPWIRE: About to write `ArgumentNullException.ThrowIfNull(x)`, `x == null`, `x is null`, or `x != null`? Check
the declaration. No `?` suffix → no check. Delete it.

## C# 14 Extension Members

This project uses C# 14 `extension(Type)` blocks, not classic `this` extension methods.

```csharp
// Instance-style extension member
public static class GuidExtensions {
    extension(Guid source) {
        public BigInteger ToBigInteger() =>
            new(source.ToByteArray(), isUnsigned: true, isBigEndian: true);
    }
}

// Static extension member (no parameter name)
public static class CancellationTokenSourceExtensions {
    extension(CancellationTokenSource) {
        public static CancellationTokenSource Linked(CancellationToken ct) =>
            CancellationTokenSource.CreateLinkedTokenSource(ct);
    }
}
```

- Known pitfall: inside an `extension(T)` block, method names can shadow the enclosing static class — fully
  qualify when ambiguous
- Reference files: `src/Kurrent.Client/Internal/Extensions/`

TRIPWIRE: About to write `public static T Method(this Type param)` (classic extension syntax)? Stop. This project
uses C# 14 `extension(Type)` blocks. Read a sibling in `Internal/Extensions/` first.

## Type Design and Sealing

- Seal classes by default unless explicitly designed for inheritance
- Seal records too — they are classes
- `readonly record struct` for value types (small, ≤16 bytes, value semantics, immutable)
- Prefer static pure functions over instance methods when no state is needed — no vtable, testable, thread-safe
- Avoid deep inheritance hierarchies — prefer flat composition with interfaces

| Use Struct When   | Use Class When       |
|-------------------|----------------------|
| Small (≤16 bytes) | Larger objects       |
| Short-lived       | Long-lived           |
| Value semantics   | Identity semantics   |
| Immutable         | Mutable state needed |

TRIPWIRE: About to write an unsealed `class` with no `virtual` members? Seal it. About to write a `class` with
only data properties? Use a `record` (or `readonly record struct` for value types).

## Collections and Enumeration

- Return `IReadOnlyList<T>` / `IReadOnlyCollection<T>` from public APIs, never `List<T>`
- Use `FrozenDictionary<K,V>` / `FrozenSet<T>` for static lookup data
- Defer `.ToList()` — single materialization at the end, never mid-chain
- Internal mutation with `List<T>` is fine — return as `IReadOnlyList<T>`

| Scenario            | Return Type                                  |
|---------------------|----------------------------------------------|
| API boundary        | `IReadOnlyList<T>`, `IReadOnlyCollection<T>` |
| Static lookup data  | `FrozenDictionary<K,V>`, `FrozenSet<T>`      |
| Internal building   | `List<T>`, return as `IReadOnlyList<T>`      |
| Single item or none | `T?` (nullable)                              |
| Lazy / streaming    | `IEnumerable<T>` or `IAsyncEnumerable<T>`    |

### API Parameter and Return Types

Accept the most abstract type that satisfies your needs. Return the most informative type the caller needs.

| Need            | Accept Parameter         | Return Type                |
|-----------------|--------------------------|----------------------------|
| Iterate once    | `IEnumerable<T>`         | `IEnumerable<T>` (if lazy) |
| Need count      | `IReadOnlyCollection<T>` | `IReadOnlyCollection<T>`   |
| Need indexing   | `IReadOnlyList<T>`       | `IReadOnlyList<T>`         |
| High-perf sync  | `ReadOnlySpan<T>`        | `Span<T>` (rarely)         |
| Async streaming | `IAsyncEnumerable<T>`    | `IAsyncEnumerable<T>`      |
| Caller mutates  | —                        | `List<T>`, `T[]`           |

## AOT Compatibility and Reflection

This project targets AOT. **All reflection is banned** — no exceptions for convenience.

Reflection breaks AOT compilation because the trimmer cannot statically determine which types, methods, and
properties are accessed at runtime. Code that uses reflection will either fail to compile under AOT or produce
runtime `MissingMethodException` / `MissingFieldException` errors.

### Banned

- **All reflection APIs**: `Type.GetMethod()`, `Type.GetField()`, `Activator.CreateInstance()`, `BindingFlags`,
  `MethodInfo.Invoke()`, `PropertyInfo.GetValue()`
- **Reflection-based libraries**: AutoMapper, Mapster, ExpressMapper, Newtonsoft.Json (default mode),
  `BinaryFormatter` (also a security risk — never use)
- **Dynamic type loading**: `Assembly.Load()`, `Type.GetType(string)`
- **Expression tree compilation**: `Expression.Compile()` at runtime
- Never embed type names in wire formats (`TypeNameHandling.All`) — breaks on rename and is a security risk

### AOT-Safe Alternatives

| Instead of                          | Use                                                                    |
|-------------------------------------|------------------------------------------------------------------------|
| AutoMapper / Mapster                | Explicit mapping methods (`ToDto()`, `ToEntity()`)                     |
| `Activator.CreateInstance<T>()`     | Factory methods, DI container, `new T()`                               |
| `Type.GetField()` / `GetProperty()` | `UnsafeAccessorAttribute` (zero overhead, AOT-safe)                    |
| Newtonsoft.Json                     | System.Text.Json with source generators                                |
| `Expression.Compile()`              | Source generators or direct code                                       |
| Runtime type inspection             | Pattern matching, generic constraints, `IVariant` discriminated unions |

### UnsafeAccessor for Private Member Access

When private/internal access is genuinely needed (serializers, test helpers), use `UnsafeAccessorAttribute` —
zero overhead, AOT-compatible, no reflection:

```csharp
// BAD: reflection — slow, allocates, breaks AOT
var field = typeof(Order).GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance);
var status = (OrderStatus)field!.GetValue(order)!;

// GOOD: UnsafeAccessor — zero overhead, AOT-compatible
[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_status")]
static extern ref OrderStatus GetStatusField(Order order);
```

Supported kinds: `Field`, `StaticField`, `Method`, `StaticMethod`, `Constructor`.

### JSON Serialization — System.Text.Json Source Generators

Always use `JsonSerializerContext` with source-generated metadata. Never rely on reflection-based
`JsonSerializer.Serialize(obj)` without a context.

```csharp
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(List<Order>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext { }

// Serialize — always pass the generated type info
var json = JsonSerializer.Serialize(order, AppJsonContext.Default.Order);

// Deserialize — always pass the generated type info
var order = JsonSerializer.Deserialize(json, AppJsonContext.Default.Order);
```

### MessagePack — AOT-Compatible Setup

Use `[MessagePackObject]` with explicit `[Key]` attributes and the source generator for AOT:

```csharp
[MessagePackObject]
public sealed class OrderEvent {
    [Key(0)] public required string Id { get; init; }
    [Key(1)] public required string StreamId { get; init; }
    [Key(2)] public required DateTimeOffset Timestamp { get; init; }
    [Key(3)] public string? Notes { get; init; }
}

// AOT: use generated resolver
var options = MessagePackSerializerOptions.Standard
    .WithResolver(CompositeResolver.Create(GeneratedResolver.Instance, StandardResolver.Instance));

var bytes = MessagePackSerializer.Serialize(evt, options);
var evt   = MessagePackSerializer.Deserialize<OrderEvent>(bytes, options);
```

### Serialization Format Guidelines

| Use Case       | Format                                  |
|----------------|-----------------------------------------|
| REST APIs      | System.Text.Json with source generators |
| gRPC           | Protocol Buffers (native)               |
| Event sourcing | Protobuf or MessagePack                 |
| Caching        | MessagePack                             |
| Configuration  | JSON (System.Text.Json)                 |

TRIPWIRE: About to write `typeof(T).GetMethod(...)`, `Activator.CreateInstance()`, `BindingFlags`, or any
`System.Reflection` API? Stop. This project targets AOT — reflection is banned. Use source generators,
`UnsafeAccessorAttribute`, explicit code, or pattern matching instead.

## Performance Patterns

### Span<T> and Memory<T>

Use `Span<T>` for synchronous zero-allocation work. Use `Memory<T>` when data must cross an `await` boundary.

```csharp
// Span for sync parsing — zero allocation
public int ParseOrderId(ReadOnlySpan<char> input) {
    if (!input.StartsWith("ORD-")) throw new FormatException("Invalid order ID format");
    return int.Parse(input.Slice(4));
}

// Memory for async — Span can't cross await
public async ValueTask<int> ReadDataAsync(Memory<byte> buffer, CancellationToken ct) => 
    await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);

// stackalloc for small temporary buffers
Span<byte> buffer = stackalloc byte[256];

// ArrayPool for larger temporary buffers (>1KB)
var buffer = ArrayPool<byte>.Shared.Rent(8192);
try {
    ProcessChunk(buffer.AsSpan(0, bytesRead));
} finally {
    ArrayPool<byte>.Shared.Return(buffer);
}

// Hybrid pattern: stackalloc when small, rent when large
[SkipLocalsInit]
static short ComputeHash(string key) {
    const int StackLimit = 256;
    
    var max = Encoding.UTF8.GetMaxByteCount(key.Length);

    byte[]? rented = null;
    Span<byte> buf = max <= StackLimit
        ? stackalloc byte[StackLimit]
        : (rented = ArrayPool<byte>.Shared.Rent(max));

    try {
        var written = Encoding.UTF8.GetBytes(key.AsSpan(), buf);
        return HashData(buf[..written]);
    } finally {
        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
    }
}
```

`[SkipLocalsInit]` skips zero-initialization of locals — use only when you write before reading the buffer.
Requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

| Type                | Use Case                                                     |
|---------------------|--------------------------------------------------------------|
| `Span<T>`           | Synchronous operations, stack-allocated buffers, slicing     |
| `ReadOnlySpan<T>`   | Read-only views, method parameters for data you won't modify |
| `Memory<T>`         | Async operations (Span can't cross await)                    |
| `ReadOnlyMemory<T>` | Read-only async operations                                   |
| `byte[]`            | Long-term storage or APIs that require arrays                |
| `ArrayPool<T>`      | Large temporary buffers (>1KB) to avoid GC pressure          |

<!-- </dotnet-conventions> -->
