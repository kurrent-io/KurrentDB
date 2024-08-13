// ReSharper disable CheckNamespace

using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Streaming;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Producers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Infrastructure;

public delegate TSnapshot UpdateSnapshot<TSnapshot, in T>(TSnapshot snapshot, T message);

public delegate TSnapshot UpdateSnapshotWithContext<TSnapshot, in T>(TSnapshot snapshot, T message, RecordContext context);

public abstract class SnapshotProjectionsModule<TSnapshot> : ProcessingModule where TSnapshot : class, IMessage, new() {
    public SnapshotProjectionsModule(Func<SystemReaderBuilder> getReaderBuilder, StreamId snapshotStreamId) {
        Reader           = getReaderBuilder().ReaderId("conn-mngt-projections-rdx").Create();
        SnapshotStreamId = snapshotStreamId;
    }

    SystemReader Reader           { get; }
    StreamId     SnapshotStreamId { get; }

    static readonly TSnapshot EmptySnapshot = new TSnapshot();

    protected void UpdateWhen<T>(UpdateSnapshot<TSnapshot, T> update) {
        Process<T>(async (evt, ctx) => {
            try {
                var (snapshot, position) = await LoadSnapshot(ctx);

                try {
                    update(snapshot, evt);
                }
                catch (Exception ex) {
                    // TODO SS: not sure if we should fail to update the snapshot atm.
                    // throw new Exception($"Failed to update {typeof(TSnapshot).Name} snapshot with {typeof(T).Name} event", ex);
                    ctx.Logger.LogCritical(ex,
                        "{SnapshotName} update failed on {EventName} event handled by user: {Event}",
                        typeof(TSnapshot).Name, typeof(T).Name, evt);
                }

                // easy wait to skip updating the snapshot
                if (snapshot == EmptySnapshot)
                    return;

                UpdateSnapshot(snapshot, position.StreamRevision, ctx);

                if (ctx.Logger.IsEnabled(LogLevel.Debug))
                    ctx.Logger.LogTrace(
                        "{SnapshotName} updated on {EventName} event: {Event}",
                        typeof(TSnapshot).Name, typeof(T).Name, evt);
            }
            catch (Exception ex) {
                ctx.Logger.LogError(ex,
                    "{SnapshotName} update failed on {EventName} event: {Event}",
                    typeof(TSnapshot).Name, typeof(T).Name, evt);
            }
        });
    }

    protected void UpdateWhen<T>(UpdateSnapshotWithContext<TSnapshot, T> update) {
        Process<T>(async (evt, ctx) => {
            try {
                var (snapshot, position) = await LoadSnapshot(ctx);

                try {
                    update(snapshot, evt, ctx);
                }
                catch (Exception ex) {
                    // TODO SS: not sure if we should fail to update the snapshot atm.
                    // throw new Exception($"Failed to update {typeof(TSnapshot).Name} snapshot with {typeof(T).Name} event", ex);
                    ctx.Logger.LogCritical(ex,
                        "{SnapshotName} update failed when {EventName} event handled by user: {Event}",
                        typeof(TSnapshot).Name, typeof(T).Name, evt);
                }

                // easy wait to skip updating the snapshot
                if (snapshot == EmptySnapshot)
                    return;

                UpdateSnapshot(snapshot, position.StreamRevision, ctx);

                if (ctx.Logger.IsEnabled(LogLevel.Debug))
                    ctx.Logger.LogTrace(
                        "{SnapshotName} updated when {EventName} event: {Event}",
                        typeof(TSnapshot).Name, typeof(T).Name, evt);
            }
            catch (Exception ex) {
                ctx.Logger.LogError(ex,
                    "{SnapshotName} update failed when {EventName} event: {Event}",
                    typeof(TSnapshot).Name, typeof(T).Name, evt);
            }
        });
    }

    async Task<(TSnapshot Snapshot, RecordPosition Position)> LoadSnapshot(RecordContext ctx) {
        try {
            var snapshotRecord = await Reader.ReadLastStreamRecord(SnapshotStreamId, ctx.CancellationToken);

            return snapshotRecord.Value is not TSnapshot snapshot
                ? (new TSnapshot(), snapshotRecord.Position)
                : (snapshot, snapshotRecord.Position);
        }
        catch (Exception ex) {
            ctx.Logger.LogError(ex, "{SnapshotName} load failed", typeof(TSnapshot).Name);
            throw;
        }
    }

    void UpdateSnapshot(TSnapshot snapshot, StreamRevision expectedRevision, RecordContext ctx) {
        var produceRequest = ProduceRequest.Builder
            .Message(snapshot
                .WithUpdateTime(TimeProvider.System.GetUtcNow())
                .WithRevision(expectedRevision + 1)
            )
            .Stream(SnapshotStreamId)
            .ExpectedStreamRevision(expectedRevision)
            .Create();

        // TODO SS: consider using a dedicated producer or a new type of processor to have full control over the output
        ctx.Output(produceRequest);
    }
}

[PublicAPI]
static class MessageExtensions {
    public static T WithUpdateTime<T>(this T message, Timestamp updateTime, bool strict = false) where T : IMessage {
        try {
            message.As<dynamic>().UpdateTime = updateTime;
        }
        catch (Exception ex) when (strict) {
            throw new Exception($"Failed to set {typeof(T).Name}.UpdateTime={updateTime.ToDateTimeOffset():O}", ex);
        }

        return message;
    }

    public static T WithUpdateTime<T>(this T message, DateTimeOffset updateTime, bool strict = false) where T : IMessage =>
        message.WithUpdateTime(updateTime.ToTimestamp(), strict);

    public static T WithUpdateTime<T>(this T message, DateTime updateTime, bool strict = false) where T : IMessage =>
        message.WithUpdateTime(updateTime.ToTimestamp(), strict);

    public static T WithRevision<T>(this T message, long revision, bool strict = false) where T : IMessage {
        try {
            message.As<dynamic>().Revision = revision;
        }
        catch (Exception ex) when (strict) {
            throw new Exception($"Failed to set {typeof(T).Name}.Revision={revision}", ex);
        }

        return message;
    }
}