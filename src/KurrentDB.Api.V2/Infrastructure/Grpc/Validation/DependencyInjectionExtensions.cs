// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scrutor;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

public static class GrpcServerBuilderExtensions {
    public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationOptions>? configure = null) {
        var options = new RequestValidationOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<RequestValidation>();
        builder.Services.TryAddSingleton<IRequestValidatorProvider, RequestValidatorProvider>();

        return builder;
    }
}

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddGrpcRequestValidatorFor<TRequest>(this IServiceCollection services) where TRequest : IMessage {
        var validatorType = typeof(IRequestValidator<TRequest>);

        services.Scan(scan => scan
            .FromApplicationDependencies()
            .AddClasses(classes => classes.AssignableTo(validatorType), publicOnly: false)
            .UsingRegistrationStrategy(RegistrationStrategy.Append)
            .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
            .WithSingletonLifetime()
        );

        return services;
    }

    public static IServiceCollection AddGrpcRequestValidator<TValidator>(this IServiceCollection services) where TValidator : class, IRequestValidator {
        // the type clauses above are not enough it must also implement IRequestValidator<>
        // otherwise someone could register a class that implements only IRequestValidator
        // which is not useful
        var validatorType = typeof(TValidator);
        if (!validatorType.IsRequestValidator())
            throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");

        services.TryAddSingleton(typeof(IRequestValidator), validatorType);

        return services;
    }

    public static IServiceCollection AddGrpcRequestValidator<TValidator>(this IServiceCollection services, TValidator validator) where TValidator : class, IRequestValidator {
        // the type clauses above are not enough it must also implement IRequestValidator<>
        // otherwise someone could register a class that implements only IRequestValidator
        // which is not useful
        var validatorType = typeof(TValidator);
        if (!validatorType.IsRequestValidator())
            throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");

        services.TryAddSingleton<IRequestValidator>(validator);

        return services;
    }
}

public class RequestValidationBuilder(IServiceCollection services) {
    public RequestValidationBuilder WithValidatorFor<TRequest>() where TRequest : IMessage {
        services.AddGrpcRequestValidatorFor<TRequest>();
        return this;
    }

    public RequestValidationBuilder WithValidator<TValidator>() where TValidator : class, IRequestValidator {
        services.AddGrpcRequestValidator<TValidator>();
        return this;
    }

    public RequestValidationBuilder WithValidator<TValidator>(TValidator validator) where TValidator : class, IRequestValidator {
        services.AddGrpcRequestValidator(validator);
        return this;
    }
}
