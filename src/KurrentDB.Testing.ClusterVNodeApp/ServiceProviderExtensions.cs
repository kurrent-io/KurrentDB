// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Testing;

public static class ServiceProviderExtensions {
    /// <summary>
    /// Retrieves the server's listening address from the service provider.
    /// It replaces wildcard hosts with 'localhost' for easier access during testing.
    /// </summary>
    public static Uri GetServerLocalAddress(this IServiceProvider services, bool https = false) {
        var uris = services
            .GetRequiredService<IServer>().Features
            .GetRequiredFeature<IServerAddressesFeature>().Addresses
            .Select(address => {
                var uri = new Uri(address);
                var host = uri.Host switch {
                    "+" or "*" => "localhost", // wildcard IPv4 host
                    "::"       => "localhost", // IPv6 unspecified
                    "[::]"     => "localhost", // IPv6 unspecified with brackets
                    _          => uri.Host
                };
                return new UriBuilder(uri) { Host = host }.Uri;
            });

        var scheme = https ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;

        return uris.First(x => x.Scheme == scheme);
    }
}
