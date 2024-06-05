using EventStore.Connectors.Control.Activation;
using EventStore.Connectors.Control.Coordination;
using EventStore.Core.Bus;
using EventStore.Streaming.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Control;

public static class ControlPlaneWireUp {
    public static void AddConnectorsControlPlane(this IServiceCollection services) {
        services.AddSingleton<SystemReader>(
            serviceProvider => {
                var publisher     = serviceProvider.GetRequiredService<IPublisher>();
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                var reader = SystemReader.Builder
                    .ReaderName("connectors-reader")
                    .Publisher(publisher)
                    .LoggerFactory(loggerFactory)
                    .EnableLogging()
                    .Create();

                return reader;
            }
        );

        services.AddConnectorsControlPlaneActivation();
        services.AddConnectorsCoordination();
    }
}