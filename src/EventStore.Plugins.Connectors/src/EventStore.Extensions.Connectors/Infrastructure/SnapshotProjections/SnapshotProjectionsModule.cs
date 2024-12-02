// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using EventStore.Streaming;
using EventStore.Streaming.Processors;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Infrastructure;

public delegate TSnapshot UpdateSnapshot<TSnapshot, in T>(TSnapshot snapshot, T message);

public delegate TSnapshot UpdateSnapshotWithContext<TSnapshot, in T>(TSnapshot snapshot, T message, RecordContext context);

[PublicAPI]
public abstract class SnapshotProjectionsModule<TSnapshot>(ISnapshotProjectionsStore store, StreamId snapshotStreamId) : ProcessingModule
    where TSnapshot : class, new() {
    ISnapshotProjectionsStore Store            { get; } = store;
    StreamId                  SnapshotStreamId { get; } = snapshotStreamId;

    protected readonly TSnapshot EmptySnapshot = new TSnapshot();

    Func<EventStoreRecord, DateTimeOffset> GetMessageTimestamp { get; set; } = record => record.Timestamp.ToUniversalTime();

    protected void SetMessageTimestampProvider(Func<EventStoreRecord, DateTimeOffset> getTimestamp) =>
        GetMessageTimestamp = getTimestamp;

    protected void UpdateWhen<T>(UpdateSnapshotWithContext<TSnapshot, T> update, Func<T, DateTimeOffset>? getTimestamp = null) where T : class, new() {
        Process<T>(async (msg, ctx) => {
            using var _ = ctx.Logger.BeginPropertyScope(("Message", msg));

            try {
                DateTimeOffset eventTimestamp;
                try {
                    eventTimestamp = getTimestamp?.Invoke(msg) ?? GetMessageTimestamp(ctx.Record);
                }
                catch (Exception ex) {
                    throw new Exception("Failed to read message timestamp", ex);
                }

                var (snapshot, position, snapshotTimestamp) = await Store.LoadSnapshot<TSnapshot>(SnapshotStreamId);

                // if (eventTimestamp <= snapshotTimestamp) {
                //     ctx.Logger.LogTrace(
                //         "Skipped update of {SnapshotName} on {EventName} because the message is older then {SnapshotTimestamp}",
                //         typeof(TSnapshot).Name, typeof(T).Name, snapshotTimestamp);
                //
                //     return;
                // }

                try {
                    update(snapshot, msg, ctx);
                }
                catch (Exception ex) {
                    throw new Exception("Failed to apply changes", ex);
                }

                // easy way to skip updating the snapshot
                if (snapshot == EmptySnapshot) {
                    ctx.Logger.LogTrace(
                        "Skipped update of {SnapshotName} v{SnapshotRevision} on {EventName} because no changes were applied",
                        typeof(TSnapshot).Name, position.StreamRevision, typeof(T).Name
                    );

                    return;
                }

                await Store.SaveSnapshot(SnapshotStreamId, position.StreamRevision, eventTimestamp, snapshot);

                ctx.Logger.LogTrace(
                    "Updated {SnapshotName} v{SnapshotRevision} on {EventName}",
                    typeof(TSnapshot).Name, position.StreamRevision, typeof(T).Name
                );
            }
            catch (Exception ex) {
                // ctx.Logger.LogError(ex,
                //     "Failed update of {SnapshotName} on {EventName}: {ErrorMessage}",
                //     typeof(TSnapshot).Name, typeof(T).Name, ex.Message
                // );

                throw new Exception($"Failed update of {typeof(TSnapshot).Name} on {typeof(T).Name}", ex);
            }
        });
    }

    protected void UpdateWhen<T>(UpdateSnapshot<TSnapshot, T> update, Func<T, DateTimeOffset>? getTimestamp = null) where T : class, new() {
        UpdateWhen((snapshot, message, _) => update(snapshot, message), getTimestamp);
    }
}