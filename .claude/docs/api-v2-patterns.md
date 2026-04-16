# API v2 Architecture and Patterns

> Read this when: working on `src/KurrentDB.Api.V2/` or `src/KurrentDB.Plugins.Api.V2/`.

## Core Infrastructure Patterns

**ApiCallback Pattern** (`src/KurrentDB.Api.V2/Infrastructure/ApiCallback.cs`):
The foundation for async message bus operations in API v2. Use this when implementing new service methods:

```csharp
var callback = ApiCallback.Create(
    state: (Request: request, Context: context),
    successPredicate: (msg) => msg is WriteEventsCompleted { Result: OperationResult.Success },
    onSuccess: (state, msg) => {
        var completed = (WriteEventsCompleted)msg;
        return new AppendSuccess { Position = completed.CommitPosition };
    }
);

publisher.Publish(new WriteEvents(correlationId, envelope: callback, ...));
return await callback.Task;
```

**ApiCommand Pattern** (`src/KurrentDB.Api.V2/Infrastructure/ApiCommand.cs`):
Base class for implementing service operations with fluent configuration:

```csharp
public class AppendCommand : ApiCommand<AppendCommand, AppendResponse> {
    protected override AppendCommand Build() {
        // Configure authorization, validation, etc.
        return this;
    }

    protected override async ValueTask<AppendResponse> Execute(CancellationToken ct) {
        // Implement the actual operation
    }
}
```

**Request Validation** (`src/KurrentDB.Api.V2/Infrastructure/Grpc/Validation/`):
All requests are validated via interceptors. Create validators by extending `RequestValidator<T>`:

```csharp
public class StreamNameValidator : RequestValidator<string> {
    public override void Validate(string value, RequestValidationContext context) {
        if (string.IsNullOrWhiteSpace(value))
            context.AddError("Stream name cannot be empty");
        if (value.StartsWith('$'))
            context.AddError("System streams cannot be written to via this API");
    }
}
```

**Error Handling** (`src/KurrentDB.Api.V2/Infrastructure/Errors/`):
Errors use protobuf annotations for automatic code generation:

```protobuf
enum StreamError {
    REVISION_CONFLICT = 1 [(kurrent.rpc.error) = {
        status_code: FAILED_PRECONDITION,
        has_details: true
    }];
}
```

Then use: `throw RpcExceptions.FromError(StreamError.REVISION_CONFLICT, errorDetails);`

## File Locations

**Infrastructure** (`src/KurrentDB.Api.V2/Infrastructure/`):
- `ApiCallback.cs`: Generic callback handler (179 lines)
- `ApiCommand.cs`: Command base class (129 lines)
- `Errors/RpcExceptions.cs`: Error mapping (200+ lines)
- `Grpc/Validation/`: Request validators and interceptors

**Service Modules** (`src/KurrentDB.Api.V2/Modules/`):
- `Streams/StreamsService.cs`: Multi-stream append implementation
- `ApiErrors.cs`: Common error factories (383 lines)

**Model** (`src/KurrentDB.Api.V2/Model/`):
- Type conversions between protobuf and internal types
- Extension methods for mapping

## Key Design Principles

1. **Generic Message Handling**: Use `ApiCallback<TState, TResponse>` for all async operations
2. **Validation First**: All inputs validated via `RequestValidator<T>` before processing
3. **Structured Errors**: Use protobuf error annotations with typed details
4. **Authorization Integration**: Check access per resource (stream, schema, etc.)
5. **Plugin Architecture**: API v2 is a plugin (`ApiV2Plugin`) for modularity

## Adding a New API v2 Service

1. Create service class extending `<ServiceName>ServiceBase`
2. Implement methods using `ApiCallback` pattern
3. Add validators for request types in `Infrastructure/Grpc/Validation/`
4. Define errors in protobuf with annotations
5. Register in `ApiV2Plugin.cs`
6. Write tests using `ClusterVNodeTestContext`
