// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using Kurrent.Toolkit;
using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Planes.Management.Queries;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamMetadata = KurrentDB.Core.Data.StreamMetadata;

namespace KurrentDB.Connectors.Planes.Management.Projectors;

[UsedImplicitly]
public class ConfigureConnectorsManagementStreams : ISystemStartupTask {
    public async Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken) {
        var publisher = serviceProvider.GetRequiredService<IPublisher>();
        var logger    = serviceProvider.GetRequiredService<ILogger<SystemStartupTaskService>>();

        await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream, maxCount: 10);
        await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionCheckpointsStream, maxCount: 10);

        return;

        Task TryConfigureStream(string stream, int maxCount) =>
            publisher
                .GetStreamMetadata(stream, cancellationToken)
                .Then(ctx => ctx.Metadata.MaxCount == maxCount
                    ? Task.FromResult(ctx)
                    : publisher.SetStreamMetadata(
                        stream,
                        new StreamMetadata(
                            maxCount:       maxCount,
                            maxAge:         ctx.Metadata.MaxAge,
                            truncateBefore: ctx.Metadata.TruncateBefore,
                            tempStream:     ctx.Metadata.TempStream,
                            cacheControl:   ctx.Metadata.CacheControl,
                            acl:            ctx.Metadata.Acl
                        ),
                        ctx.Revision,
                        cancellationToken
                    )
                )
                .OnError(ex => logger.LogError(ex, "{TaskName} Failed to configure stream {Stream}", nameof(ConfigureConnectorsManagementStreams), stream))
                .Then(
                    state => state.Logger.LogDebug("{TaskName} Stream {Stream} configured", nameof(ConfigureConnectorsManagementStreams), state.Stream),
                    (Logger: logger, Stream: stream)
                );
    }
}
