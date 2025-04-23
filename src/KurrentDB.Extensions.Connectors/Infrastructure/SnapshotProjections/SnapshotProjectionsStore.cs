// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using KurrentDB.Connect.Producers;
using KurrentDB.Connect.Producers.Configuration;
using KurrentDB.Connect.Readers;
using KurrentDB.Connect.Readers.Configuration;
using Kurrent.Surge;
using Kurrent.Surge.Producers;
using Kurrent.Toolkit;

namespace KurrentDB.Connectors.Infrastructure;

public interface ISnapshotProjectionsStore {
    Task<(TSnapshot Snapshot, RecordPosition Position, DateTimeOffset Timestamp)> LoadSnapshot<TSnapshot>(StreamId snapshotStreamId) where TSnapshot : class, new();
    Task SaveSnapshot<TSnapshot>(StreamId snapshotStreamId, StreamRevision expectedRevision, DateTimeOffset Timestamp, TSnapshot snapshot) where TSnapshot : class, new();
}

public class SystemSnapshotProjectionsStore(
    Func<SystemReaderBuilder> getReaderBuilder,
    Func<SystemProducerBuilder> getProducerBuilder
) : ISnapshotProjectionsStore {
    SystemReader   Reader   { get; } = getReaderBuilder().ReaderId("SystemSnapshotProjectionsStoreReader").Create();
    SystemProducer Producer { get; } = getProducerBuilder().ProducerId("SystemSnapshotProjectionsStoreProducer").Create();

    const string SnapshotTimestampHeaderKey = "esdb.snapshot.timestamp";

    public async Task<(TSnapshot Snapshot, RecordPosition Position, DateTimeOffset Timestamp)> LoadSnapshot<TSnapshot>(StreamId snapshotStreamId) where TSnapshot : class, new() {
        try {
            var snapshotRecord = await Reader.ReadLastStreamRecord(snapshotStreamId); // dont cancel here...

            return snapshotRecord.Value is not TSnapshot snapshot
                ? (new TSnapshot(), snapshotRecord.Position, DateTimeOffset.MinValue)
                : (snapshot, snapshotRecord.Position, DateTimeOffset.Parse(snapshotRecord.Headers[SnapshotTimestampHeaderKey]!));
        }
        catch (Exception ex) {
            throw new Exception($"Unable to load snapshot from stream {snapshotStreamId}", ex);
        }
    }

    public async Task SaveSnapshot<TSnapshot>(StreamId snapshotStreamId, StreamRevision expectedRevision, DateTimeOffset timestamp, TSnapshot snapshot) where TSnapshot : class, new() {
        var produceRequest = ProduceRequest.Builder
            .Message(snapshot)
            .Headers(headers => headers[SnapshotTimestampHeaderKey] = timestamp.ToIso8601())
            .Stream(snapshotStreamId)
            .ExpectedStreamRevision(expectedRevision)
            .Create();

        try {
            await Producer.Produce(produceRequest, throwOnError: true);
        }
        catch (Exception ex) {
            throw new Exception($"Unable to save snapshot to stream {snapshotStreamId} with expected revision v{expectedRevision}", ex);
        }
    }
}
