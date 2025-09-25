namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

/// <summary>
/// A delegate that defines a method signature for intercepting gRPC requests.
/// </summary>
public delegate ValueTask InterceptRequest<in TRequest>(TRequest request);

/// <summary>
/// A delegate that defines a method signature for intercepting gRPC requests with additional state.
/// </summary>
public delegate ValueTask InterceptRequest<in TRequest, in TState>(TRequest request, TState state);
