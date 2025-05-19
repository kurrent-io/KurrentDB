using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using EventStore.Client;
using KurrentDB.Surge.Testing.Containers.FluentDocker;
using KurrentDB.Surge.Testing.Extensions;
using Serilog;
using Serilog.Extensions.Logging;

namespace KurrentDB.Surge.Testing.Containers.KurrentDB;

[PublicAPI]
public class KurrentDbTestContainer() : TestContainer("eventstore/eventstore:latest") {
    //cloudsmith
    // const string DefaultArmImage = "docker.eventstore.com/eventstore-preview/eventstoredb-ee:24.10.0-experimental-arm64-8.0-jammy";
    // const string DefaultArmImage = "eventstore/eventstore:24.10.1-alpha-arm64v8";

    const int DefaultPort = 2113;

    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "changeit";

    protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) {
        var environment = new Dictionary<string, string> {
            ["EVENTSTORE_TELEMETRY_OPTOUT"]           = "true",
            ["EVENTSTORE_INSECURE"]                   = "true",
            ["EVENTSTORE_MEM_DB"]                     = "true",
            ["EVENTSTORE_RUN_PROJECTIONS"]            = "None",
            ["EVENTSTORE_START_STANDARD_PROJECTIONS"] = "false",
            ["EVENTSTORE_LOG_LEVEL"]                  = "Default", // required to use serilog settings
            ["EVENTSTORE_DISABLE_LOG_FILE"]           = "true",
            ["EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP"]  = "true" // required to use legacy UI
        };

        // return builder;

        return builder
            .WithEnvironment(environment)
            .ExposePort(DefaultPort, DefaultPort)
            .KeepContainer().KeepRunning().ReuseIfExists()
            .WaitUntilReadyWithConstantBackoff(1_000, 60, service => {
                var output = service.ExecuteCommand("curl -o - -I http://admin:changeit@localhost:2113/health/live");
                if (!output.Success)
                    throw new Exception(output.Error);
            });
    }

    public string AuthenticatedConnectionString { get; private set; } = null!;
    public string AnonymousConnectionString     { get; private set; } = null!;

    protected override ValueTask OnStarted() {
        var endpoint   = Service.GetPublicEndpoint(DefaultPort);
        var uriBuilder = new UriBuilder("esdb", endpoint.Address.ToString(), endpoint.Port) {
            Query = "tls=false"
        };

        AnonymousConnectionString = uriBuilder.ToString();

        AuthenticatedConnectionString = uriBuilder
            .With(x => x.UserName = DefaultUsername)
            .With(x => x.Password = DefaultPassword)
            .ToString();

        return ValueTask.CompletedTask;
    }

    public EventStoreClientSettings GetClientSettings() => EventStoreClientSettings
        .Create(AuthenticatedConnectionString)
        .With(x => {
            x.LoggerFactory = new SerilogLoggerFactory(Log.Logger);
            x.ConnectivitySettings.KeepAliveInterval = TimeSpan.FromSeconds(60);
            x.ConnectivitySettings.KeepAliveTimeout  = TimeSpan.FromSeconds(30);
            x.CreateHttpMessageHandler = () => CreateOptimizedHandler(x);
        });

    public EventStoreClientSettings GetAnonymousClientSettings() => EventStoreClientSettings
        .Create(AnonymousConnectionString)
        .With(x => {
            x.LoggerFactory                          = new SerilogLoggerFactory(Log.Logger);
            x.ConnectivitySettings.KeepAliveInterval = TimeSpan.FromSeconds(60);
            x.ConnectivitySettings.KeepAliveTimeout  = TimeSpan.FromSeconds(30);
            x.CreateHttpMessageHandler               = () => CreateOptimizedHandler(x);
        });

    static SocketsHttpHandler CreateOptimizedHandler(EventStoreClientSettings settings) {
        var handler = new SocketsHttpHandler {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay             = settings.ConnectivitySettings.KeepAliveInterval,
            KeepAlivePingTimeout           = settings.ConnectivitySettings.KeepAliveTimeout,
            PooledConnectionIdleTimeout    = System.Threading.Timeout.InfiniteTimeSpan,
            KeepAlivePingPolicy            = HttpKeepAlivePingPolicy.Always,

            SslOptions = new SslClientAuthenticationOptions {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        if (settings.ConnectivitySettings.Insecure)
            return handler;

        if (settings.ConnectivitySettings.ClientCertificate != null)
            handler.SslOptions.ClientCertificates = new X509CertificateCollection {
                settings.ConnectivitySettings.ClientCertificate
            };

        handler.SslOptions.RemoteCertificateValidationCallback = settings.ConnectivitySettings.TlsVerifyCert
            ? settings.ConnectivitySettings.TlsCaFile is null
                ? null
                : (_, certificate, chain, _) => {
                    if (certificate is not X509Certificate2 certificate2 || chain is null)
                        return false;

                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(settings.ConnectivitySettings.TlsCaFile);
                    return chain.Build(certificate2);
                }
            : (_, _, _, _) => true;

        return handler;
    }

    public EventStoreClient GetClient(string connectionName = null) =>
        new EventStoreClient(GetClientSettings().With(x => x.ConnectionName = connectionName ?? "toolkit"));

    public EventStoreClient GetAnonymousClient(string connectionName = null) =>
        new EventStoreClient(GetAnonymousClientSettings().With(x => x.ConnectionName = connectionName ?? "toolkit"));
}
