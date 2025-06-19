// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using Kurrent.Surge.Producers;
using KurrentDB.Connect.Producers.Configuration;
using KurrentDB.Connect.Readers.Configuration;
using KurrentDB.Connectors.Control.Contracts;
using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Planes.Control;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Planes.Management.Migrations;

public class FixConnectorsControlRegistryStreamName : ISystemStartupTask {
public async Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken) {
        var publisher          = serviceProvider.GetRequiredService<IPublisher>();
        var logger             = serviceProvider.GetRequiredService<ILogger<SystemStartupTaskService>>();
        var getReaderBuilder   = serviceProvider.GetRequiredService<Func<SystemReaderBuilder>>();
        var getProducerBuilder = serviceProvider.GetRequiredService<Func<SystemProducerBuilder>>();
        var options            = serviceProvider.GetRequiredService<ConnectorsControlRegistryOptions>();

        var reader   = getReaderBuilder().ReaderId(nameof(FixConnectorsControlRegistryStreamName)).Create();
        var producer = getProducerBuilder().ProducerId(nameof(FixConnectorsControlRegistryStreamName)).Create();

        var snapshotRecord = await reader.ReadLastStreamRecord(options.SnapshotStreamId, cancellationToken);
        if (snapshotRecord.Value is ActivatedConnectorsSnapshot)
            return;

        var unwantedStreamName = $"${options.SnapshotStreamId}";
        var unwantedSnapshotRecord = await reader.ReadLastStreamRecord(unwantedStreamName, cancellationToken);

        if (unwantedSnapshotRecord.Value is not ActivatedConnectorsSnapshot previousSnapshot)
            return;

        // the incorrect name was $$connectors-ctrl/registry-snapshots
        // it has been corrected to $connectors-ctrl/registry-snapshots
        // we soft delete the connectors-ctrl/registry-snapshots stream, which the incorrect stream is the metadata stream of
        logger.LogInformation("Fixing previous incorrect connector registry stream name");
        var request = ProduceRequest.Builder
            .Message(previousSnapshot)
            .Stream(options.SnapshotStreamId)
            .ExpectedStreamState(StreamState.Any)
            .Create();

        await producer.Produce(request);
        logger.LogInformation("Successfully migrated pre-existing connector registry snapshot ");

        var unwantedRegularStreamName = options.SnapshotStreamId.Value[1..];
        await publisher.SoftDeleteStream(unwantedRegularStreamName, ExpectedVersion.NoStream, cancellationToken)
            .OnError(ex => logger.LogWarning("Did not delete incorrect connector registry stream: {Error}", ex));

        logger.LogInformation("Migration of incorrect connector registry stream completed.");
    }
}
