// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using Google.Protobuf.Reflection;
using Google.Rpc;
using Grpc.AspNetCore.Server;
using Kurrent.Rpc;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.DependencyInjection;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Modules.CustomIndexes;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core;
using KurrentDB.Protocol.V2.CustomIndexes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamsService = KurrentDB.Api.Streams.StreamsService;

namespace KurrentDB.Plugins.Api.V2;

//qq admin api working in a cluster?
[UsedImplicitly]
public class ApiV2Plugin() : SubsystemsPlugin("APIV2") {
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services
            .AddGrpc()
            .AddJsonTranscoding(options => {
                options.TypeRegistry = TypeRegistry.FromFiles(
                    CustomIndexesReflection.Descriptor,
                    KurrentDB.Protocol.V2.CustomIndexes.Errors.ErrorsReflection.Descriptor,
                    ErrorsReflection.Descriptor,
                    RpcReflection.Descriptor,
                    BadRequest.Descriptor.File,
                    ErrorInfo.Descriptor.File,
                    DebugInfo.Descriptor.File,
                    PreconditionFailure.Descriptor.File,
                    QuotaFailure.Descriptor.File,
                    ResourceInfo.Descriptor.File,
                    RetryInfo.Descriptor.File);
            })
            .WithRequestValidation(x => x.ExceptionFactory = ApiErrors.InvalidRequest)
            .WithGrpcService<CustomIndexesGrpcService>()
            .WithGrpcService<StreamsService>(
                validation => validation.WithValidator<AppendRequestValidator>());

        // Configure the StreamsService to allow large messages based on the server settings
        services.Configure<GrpcServiceOptions<StreamsService>>((sp, options) => {
            var serverOptions = sp.GetRequiredService<ClusterVNodeOptions>();

            // MaxReceiveMessageSize must always be larger than the max append size
            // so that the server can return proper error messages when the client
            // exceeds the limit.
            // For example, if the max append size is 8MB, and we use a 50% buffer,
            // the max receive message size will be set to 12MB.
            options.MaxReceiveMessageSize = (int)(serverOptions.Application.MaxAppendSize * 1.50);
        });
    }

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = false });

        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<CustomIndexesGrpcService>()
                .EnableGrpcWeb();
            endpoints.MapGrpcService<StreamsService>()
                .EnableGrpcWeb();
        });
	}
}
