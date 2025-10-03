// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.DependencyInjection;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
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
                val => val.AddValidatorFor<AppendRequest>(),
                svc => svc.MaxReceiveMessageSize = TFConsts.MaxLogRecordSize
            );
    }

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseRouting();

        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        app.UseEndpoints(endpoints => {
            endpoints.MapGrpcService<StreamsService>();
        });
	}
}
