// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using Kurrent.Surge;
using Kurrent.Surge.DataProtection;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.Logging;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions.Streams;

using StreamMetadata = KurrentDB.Core.Data.StreamMetadata;

namespace KurrentDB.Connectors.Planes.Management;

[PublicAPI]
public record ConnectorsStreamSupervisorOptions {
    public SystemStreamOptions Leases      { get; init; }
    public SystemStreamOptions Checkpoints { get; init; }
}

public record SystemStreamOptions(int? MaxCount = null, TimeSpan? MaxAge = null) {
    public StreamMetadata AsStreamMetadata() => new(maxCount: MaxCount, maxAge: MaxAge);
}

/// <summary>
/// Responsible for configuring and deleting the system streams for a connector.
/// </summary>
[PublicAPI]
public class ConnectorsStreamSupervisor(ConnectorsStreamSupervisorOptions options, IPublisher client, IDataProtector protector, ILogger<ConnectorsStreamSupervisor> logger) {
    IPublisher                          Client              { get; } = client;
    IDataProtector                      Protector           { get; } = protector;
    ILogger<ConnectorsStreamSupervisor> Logger              { get; } = logger;
    StreamMetadata                      LeasesMetadata      { get; } = options.Leases.AsStreamMetadata();
    StreamMetadata                      CheckpointsMetadata { get; } = options.Checkpoints.AsStreamMetadata();

    public async ValueTask<bool> ConfigureConnectorStreams(string connectorId, CancellationToken ct) {
        await Task.WhenAll(
            // TryConfigureStream(GetLeasesStream(connectorId), LeasesMetadata),
            TryConfigureStream(GetCheckpointsStream(connectorId), CheckpointsMetadata)
        );

        return true;

        Task TryConfigureStream(string stream, StreamMetadata metadata) => Client
            .SetStreamMetadata(stream, metadata, cancellationToken: ct)
            .OnError(ex => Logger.LogError(ex, "{ProcessorId} Failed to configure stream {Stream}", connectorId, stream))
            .Then(state => state.Logger.LogDebug("{ProcessorId} Stream {Stream} configured {Metadata}", connectorId, state.Stream, state.Metadata), (Logger, Stream: stream, Metadata: metadata));
    }

    public bool ConfigureConnectorStreams(string connectorId) =>
        ConfigureConnectorStreams(connectorId, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public async ValueTask<bool> DeleteConnectorStreams(string connectorId, RecordPosition expectedPosition, CancellationToken ct) {
        await Task.WhenAll(
            // TryDeleteStream(GetLeasesStream(connectorId)),
            TryDeleteStream(GetCheckpointsStream(connectorId))

            // we found a last minute bug here before releasing v25.0.0
            // we will address this later with a cleanup task on migration
            // Protector.Forget(connectorId, ct).AsTask()
        );

        return true;

        Task TryDeleteStream(string stream) => Client
            .SoftDeleteStream(stream, ct)
            .OnError(ex => Logger.LogError(ex, "{ProcessorId} Failed to delete stream {Stream}", connectorId, stream))
            .Then(state => state.Logger.LogInformation("{ProcessorId} Stream {Stream} deleted", connectorId, state.Stream), (Logger, Stream: stream));
    }

    public bool DeleteConnectorStreams(string connectorId) =>
        DeleteConnectorStreams(connectorId, RecordPosition.Unset, CancellationToken.None).AsTask().GetAwaiter().GetResult();
}
