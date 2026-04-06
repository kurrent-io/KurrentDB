# Protocol v2 Structure

> Read this when: working on protocol buffers in `/proto`, defining new gRPC services, or adding error types.

## Protocol Buffer Organization

**Location**: All protocol buffers are in `/proto` directory

**Kurrent RPC Framework** (`proto/kurrent/rpc/`):
- `rpc.proto`: Error metadata annotations for enum values
- `errors.proto`: Server-level error definitions (ACCESS_DENIED, NOT_LEADER_NODE, etc.)
- `code.proto`: Google RPC canonical error codes

**Streams Protocol v2** (`proto/kurrentdb/protocol/v2/streams/`):
- `streams.proto`: Multi-stream append operations, append sessions
- `errors.proto`: Stream-specific errors (REVISION_CONFLICT, STREAM_DELETED, etc.)
- Properties: `event_type`, `data_format`, `schema_id`, custom properties

**Registry Protocol v2** (`proto/kurrentdb/protocol/v2/registry/`):
- `schemas.proto`: Schema CRUD operations
- `groups.proto`: Schema group management
- `service.proto`: gRPC service definitions
- `events.proto`: Schema lifecycle events
- `shared.proto`: Common types (compatibility modes, data formats)
- `errors.proto`: Registry-specific errors

## Error Annotation Pattern

Define errors in protobuf with metadata:

```protobuf
import "kurrent/rpc/rpc.proto";

enum MyError {
  VALIDATION_FAILED = 1 [(kurrent.rpc.error) = {
    status_code: INVALID_ARGUMENT,
    has_details: true
  }];

  NOT_FOUND = 2 [(kurrent.rpc.error) = {
    status_code: NOT_FOUND,
    has_details: false
  }];
}

message ValidationFailedDetails {
  repeated string field_errors = 1;
}
```

Then use in code:
```csharp
throw RpcExceptions.FromError(MyError.VALIDATION_FAILED,
    new ValidationFailedDetails { FieldErrors = { "Invalid format" } });
```

## Code Generation

- **Generation**: Automatic during build via `KurrentDB.Protocol` project
- Error metadata extraction happens at runtime via reflection
- No manual code generation needed for error handling

## Adding a New Protocol Buffer Message

1. Add `.proto` file in appropriate directory under `/proto`
2. Use error annotations if defining errors
3. Build project - code generation happens automatically
4. Import generated types in C# code
