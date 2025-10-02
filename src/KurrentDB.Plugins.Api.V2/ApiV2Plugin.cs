// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams;
using KurrentDB.Core;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StreamsService = KurrentDB.Api.Streams.StreamsService;

namespace KurrentDB.Plugins.Api.V2;

[UsedImplicitly]
public class ApiV2Plugin() : SubsystemsPlugin("APIV2") {
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services.AddNodeSystemInfoProvider();

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
            .AddServiceOptions<StreamsService>(options => {
                // Set max message size to chunk size + max log record size to allow
                // appending a full chunk of data without hitting gRPC limits.
                // Individual record size limits are still enforced separately.
                // This way the operation can validate the request instead of gRPC
                // rejecting it outright.
                options.MaxReceiveMessageSize = TFConsts.ChunkSize + TFConsts.EffectiveMaxLogRecordSize + 1024;
            })
            .AddRequestValidation(
                options => options.ExceptionFactory = (_, errors) => ApiErrors.InvalidRequest(errors.ToArray()),
                validation => {
                    validation.AddValidatorFor<AppendRecord>();
                    validation.AddValidatorFor<AppendRequest>();
                }
            );

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
