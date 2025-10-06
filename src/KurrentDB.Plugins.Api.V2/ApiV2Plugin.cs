// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using Grpc.AspNetCore.Server;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.DependencyInjection;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamsService = KurrentDB.Api.Streams.StreamsService;

namespace KurrentDB.Plugins.Api.V2;

[UsedImplicitly]
public class ApiV2Plugin() : SubsystemsPlugin("APIV2") {
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services.AddNodeSystemInfoProvider();

        services
            .AddGrpc()
            .WithRequestValidation(x => x.ExceptionFactory = ApiErrors.InvalidRequest)
            .WithGrpcService<StreamsService>(
                validation => validation.WithValidator<AppendRequestValidator>());

        // Configure the StreamsService to allow large messages based on the server settings
        services.Configure<GrpcServiceOptions<StreamsService>>((sp, options) => {
            var serverOptions = sp.GetRequiredService<ClusterVNodeOptions>();

            // MaxReceiveMessageSize must always be  larger than the max append event
            // size so that the server can return proper error messages when the client
            // exceeds the limit.
            // For example, if the max append event size is 8MB, the max receive message size
            // will be 8.4MB if we use a 5% buffer.
            options.MaxReceiveMessageSize = (int)(serverOptions.Application.MaxAppendEventSize * 1.05);
        });

        Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_ENABLE_GRPC_INSTRUMENTATION", "True");

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter("Grpc.AspNetCore.Server"));
    }

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = false });

        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<StreamsService>()
                .EnableGrpcWeb();
        });
	}
}
