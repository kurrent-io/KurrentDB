// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.AspNetCore.Server;
using KurrentDB.Api.Infrastructure.Grpc.Compression;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KurrentDB.Api.Infrastructure.DependencyInjection;

public static class GrpcServerBuilderExtensions {
    public static IGrpcServerBuilder WithGrpcService<TService>(
        this IGrpcServerBuilder builder,
        Action<RequestValidationBuilder>? configureValidation = null,
        Action<GrpcServiceOptions<TService>>? configureGrpc = null
    ) where TService : class {
        builder.Services.TryAddSingleton<TService>();

        configureValidation?.Invoke(new RequestValidationBuilder(builder.Services));

        builder.AddServiceOptions<TService>(options => {
            options.WithCompression();
            options.Interceptors.Add<RequestValidationInterceptor>();
            configureGrpc?.Invoke(options);
        });

        return builder;
    }
}
