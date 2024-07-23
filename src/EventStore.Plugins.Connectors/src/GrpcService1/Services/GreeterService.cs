using Grpc.Core;
using GrpcService1;

namespace GrpcService1.Services;

/// <inheritdoc />
public class GreeterService : Greeter.GreeterBase {
    private readonly ILogger<GreeterService> _logger;

    /// <inheritdoc />
    public GreeterService(ILogger<GreeterService> logger) {
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context) {
        return Task.FromResult(
            new HelloReply {
                Message = "Hello " + request.Name
            }
        );
    }
}