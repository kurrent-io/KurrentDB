using Grpc.AspNetCore.Server;
using KurrentDB.Api.Infrastructure.Grpc.Compression;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KurrentDB.Api.Infrastructure.DependencyInjection;

public static class GrpcServerBuilderExtensions {
    public static void WithGrpcService<TService>(
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
    }
}
