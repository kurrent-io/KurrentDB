// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public static IServiceCollection AddGrpcRequestValidator<TValidator>(this IServiceCollection services) where TValidator : class, IRequestValidator {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestValidator, TValidator>());
        return services;
    }

    public static IServiceCollection AddGrpcRequestValidator<TValidator>(this IServiceCollection services, TValidator validator) where TValidator : class, IRequestValidator {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestValidator>(validator));
        return services;
    }
}

public class RequestValidationBuilder(IServiceCollection services) {
    public RequestValidationBuilder WithValidator<TValidator>() where TValidator : class, IRequestValidator {
        services.AddGrpcRequestValidator<TValidator>();
        return this;
    }

    public RequestValidationBuilder WithValidator<TValidator>(TValidator validator) where TValidator : class, IRequestValidator {
        services.AddGrpcRequestValidator(validator);
        return this;
    }
}
