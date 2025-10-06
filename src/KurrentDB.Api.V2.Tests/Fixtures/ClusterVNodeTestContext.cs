// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Grpc.Net.ClientFactory;
using Humanizer;
using KurrentDB.Core;
using KurrentDB.Testing.TUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using TUnit.Core.Interfaces;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Tests.Fixtures;

[PublicAPI]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class ClusterVNodeTestContext : IAsyncInitializer, IAsyncDisposable {
    public ClusterVNodeTestContext() {
        Server = new ClusterVNodeApp(ConfigureServices, ConfigurationOverrides);

        ServerOptions = Server.ServerOptions;
        Services      = Server.Services;
    }

    static readonly Dictionary<string, object?> ConfigurationOverrides = new() {
        { "KurrentDB:Application:MaxAppendEventSize", 8.Megabytes().Bytes },
        { "KurrentDB:Application:MaxAppendSize", 16.Megabytes().Bytes }
    };

    static void ConfigureServices(ClusterVNodeOptions options, IServiceCollection services) {
        services
            .AddSingleton<ILoggerFactory, ToolkitTestLoggerFactory>()
            //.AddTestLogging()
            .AddTestTimeProvider();

        services.ConfigureAll<HttpClientFactoryOptions>(factory => {
            // //this must be switched on before creation of the HttpMessageHandler
            // AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            factory.HttpMessageHandlerBuilderActions.Add(builder => {
                if (builder.PrimaryHandler is SocketsHttpHandler { } handler) {
                    handler.AutomaticDecompression = DecompressionMethods.All;
                    handler.SslOptions             = new() { RemoteCertificateValidationCallback = (_, _, _, _) => true };
                }
            });
        });

        services.ConfigureAll<GrpcClientFactoryOptions>(factory =>
            factory.ChannelOptionsActions.Add(channel => {
                channel.UnsafeUseInsecureChannelCallCredentials = true;
            })
        );

        // ====================================================================
        // gRPC clients for every service that is available on the server
        // ====================================================================

        services.AddGrpcClient<StreamsServiceClient>();
    }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    ClusterVNodeApp Server { get; set; }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public ClusterVNodeOptions ServerOptions { get; }

    /// <summary>
    /// The actual test server instance representing a cluster node.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILogger Logger => TestContext.Current.Logger();

    /// <summary>
    /// Pre-configured Faker instance for generating test data.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestContext.Current.LoggerFactory();

    /// <summary>
    /// The time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; private set; } = null!;

    /// <summary>
    /// The client for interacting with the system bus.
    /// </summary>
    public ISystemClient SystemClient { get; private set; } = null!;

    /// <summary>
	/// The gRPC client for the Streams service.
	/// </summary>
	public StreamsServiceClient StreamsClient { get; private set; } = null!;

    /// <summary>
    /// Initializes the test server and related resources.
    /// This method sets up the server, starts it, and
    /// configures all the grpc service clients.
    /// </summary>
    public async Task InitializeAsync() {
        await Server.Start();

        Time         = Server.Services.GetRequiredService<FakeTimeProvider>();
        SystemClient = Server.Services.GetRequiredService<ISystemClient>();

        // ====================================================================
        // Resolve all gRPC clients
        // ====================================================================

        StreamsClient = Server.Services.GetRequiredService<StreamsServiceClient>();
    }

    public async ValueTask DisposeAsync() =>
        await Server.DisposeAsync();

    string GenerateJwtToken(string username) {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var claims      = new[] { new Claim(ClaimTypes.Name, username) };
        var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("KurrentDB", "Clients", claims, signingCredentials: credentials);

        return JwtTokenHandler.WriteToken(token);
    }

    static readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
    static readonly SymmetricSecurityKey    SecurityKey     = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
}
