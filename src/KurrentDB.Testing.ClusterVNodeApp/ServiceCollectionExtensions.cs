// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Testing;

public static class ServiceCollectionExtensions {
    /// <summary>
    /// Configures gRPC client address discovery to use a static resolver
    /// that points to the test server's address.
    /// This is useful for integration tests where the server address
    /// is dynamically assigned and needs to be discovered by the gRPC client.
    /// </summary>
    public static IServiceCollection EnableGrpcClientsAddressDiscovery(this IServiceCollection services, string uriSuffix = "kurrentdb") {
        ArgumentException.ThrowIfNullOrWhiteSpace(uriSuffix);

        services.AddSingleton<ResolverFactory>(ctx => {
            var serverAddress   = ctx.GetServerLocalAddress();
            var balancerAddress = new BalancerAddress(serverAddress.Host, serverAddress.Port);
            return new StaticResolverFactory(_ => [balancerAddress]);
        });

        services.ConfigureAll<GrpcClientFactoryOptions>(factory => {
            factory.Address = new Uri($"static://{uriSuffix}");
            factory.ChannelOptionsActions
                .Add(channel => channel.Credentials = ChannelCredentials.Insecure);
        });

        return services;
    }
}
