using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SchemaRegistry.Infrastructure.Grpc;

public class GrpcRequestValidator(IServiceProvider serviceProvider) {
    public ValidationResult Validate<T>(T request) where T : IMessage {
        var validationResult = TryValidate(request);
        if (validationResult is null)
            throw new InvalidOperationException($"No validator found for {request!.GetType().Name}");

        return validationResult;
    }
    
    public ValidationResult? TryValidate<T>(T request) where T : IMessage {
        var validator = serviceProvider.GetService<IValidator<T>>();
        return validator?.Validate(request);
    }

    public void EnsureValid<T>(T request) where T : IMessage {
        var result = Validate(request);
        if (!result.IsValid)
            throw RpcExceptions.InvalidArgument(result);
    }
}

public static class GrpcRequestValidatorExtensions {
    public static void AddGrpcRequestValidation(this IServiceCollection services, Assembly? assembly = null) =>
        services
            .AddValidatorsFromAssembly(assembly ?? Assembly.GetExecutingAssembly(), ServiceLifetime.Singleton)
            .AddSingleton<GrpcRequestValidator>();
}