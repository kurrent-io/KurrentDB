// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Plugins.Api.V2;

[UsedImplicitly]
public class ApiV2Plugin() : SubsystemsPlugin("APIV2") {
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(ctx => {
            var serverOptions = ctx.GetRequiredService<ClusterVNodeOptions>();
            return new AppendRecordValidatorOptions(serverOptions.Application.MaxAppendEventSize);
        });

        services.AddNodeSystemInfoProvider(); // required cause its a new one...

        services
            .AddGrpc(options => {
                var serviceProvider = services.BuildServiceProvider();
                var serverOptions   = serviceProvider.GetRequiredService<ClusterVNodeOptions>();
                var hostEnvironment = serviceProvider.GetRequiredService<IHostEnvironment>();

                options.EnableDetailedErrors = hostEnvironment.IsDevelopment()
                                            || hostEnvironment.IsStaging()
                                            || serverOptions.DevMode.Dev;

                options.Interceptors.Add<RequestValidationInterceptor>();
            })
            .AddRequestValidation(new RequestValidationOptions {
                ExceptionFactory = (_, errors) => ApiErrors.InvalidRequest(errors.ToArray())
            });

        services
            .AddSingleton<StreamsService>()
            .AddSingleton<StreamsServiceOptions>(ctx => {
                var serverOptions = ctx.GetRequiredService<ClusterVNodeOptions>();
                return new StreamsServiceOptions {
                    MaxAppendSize = serverOptions.Application.MaxAppendSize,
                    MaxRecordSize = serverOptions.Application.MaxAppendEventSize
                };
            });
    }

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseRouting();

        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<StreamsService>();
        });
	}
}
