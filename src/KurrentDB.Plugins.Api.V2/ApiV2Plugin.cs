// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StreamsService = KurrentDB.Api.Streams.StreamsService;

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
                }
            )
            .AddServiceOptions<StreamsService>(options => options.MaxReceiveMessageSize = TFConsts.ChunkSize)
            .AddRequestValidation(
                new RequestValidationOptions {
                    ExceptionFactory = (_, errors) => ApiErrors.InvalidRequest(errors.ToArray())
                }, validation => {
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

        // Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", "KurrentDB.API");

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddHostDetector()
                // .AddProcessDetector()
                // .AddProcessRuntimeDetector()
                .AddContainerDetector()
                .AddEnvironmentVariableDetector()
                // .AddOperatingSystemDetector()
                .AddService("KurrentDB.API", serviceVersion: "2.0.0")
                // .AddTelemetrySdk()
            )
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddEventCountersInstrumentation(options =>
                {
                    options.AddEventSources("Grpc.AspNetCore.Server");
                })
                // .AddProcessInstrumentation()
                // .AddRuntimeInstrumentation()
                // .AddMeter("Grpc.AspNetCore.Server")
                // .AddMeter("Microsoft.AspNetCore.Hosting")
                // .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                // .AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf)
                .AddOtlpExporter());
            // .WithTracing(tracing => tracing
            //     .AddAspNetCoreInstrumentation()
            //     .AddOtlpExporter());
    }

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseRouting();

        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<StreamsService>();
        });
	}
}
