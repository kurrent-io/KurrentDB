using EventStore.Connectors.Management.Queries;
using EventStore.Connectors.System;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Projectors;

[UsedImplicitly]
public class ConfigureConnectorsManagementStreams : ISystemStartupTask {
    public async Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken) {
        var publisher = serviceProvider.GetRequiredService<IPublisher>();
        var logger    = serviceProvider.GetRequiredService<ILogger<SystemStartupTaskService>>();

        await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream, maxCount: 10);
        await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionCheckpointsStream, maxCount: 10);

        return;

        Task TryConfigureStream(string stream, int maxCount) {
            return publisher
                .GetStreamMetadata(stream, cancellationToken)
                .Then(result => {
                    var (metadata, revision) = result;

                    if (metadata.MaxCount == maxCount)
                        return Task.FromResult(result);

                    var newMetadata = new StreamMetadata(
                        maxCount: maxCount,
                        maxAge: metadata.MaxAge,
                        truncateBefore: metadata.TruncateBefore,
                        tempStream: metadata.TempStream,
                        cacheControl: metadata.CacheControl,
                        acl: metadata.Acl
                    );

                    return publisher.SetStreamMetadata(stream, newMetadata, revision, cancellationToken);
                })
                .OnError(ex => logger.LogError(ex, "{TaskName} Failed to configure projection stream {Stream}", nameof(ConfigureConnectorsManagementStreams), stream))
                .Then(state =>
                    state.Logger.LogDebug(
                        "{TaskName} projection stream {Stream} configured",
                        nameof(ConfigureConnectorsManagementStreams), state.Stream
                    ), (Logger: logger, Stream: stream)
                );
        }
    }
}