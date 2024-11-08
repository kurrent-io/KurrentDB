// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using EventStore.Connect.Producers;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Streaming;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Producers;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Infrastructure;

public delegate TSnapshot UpdateSnapshot<TSnapshot, in T>(TSnapshot snapshot, T message);

public delegate TSnapshot UpdateSnapshotWithContext<TSnapshot, in T>(TSnapshot snapshot, T message, RecordContext context);

public abstract class SnapshotProjectionsModule<TSnapshot> : ProcessingModule where TSnapshot : class, new() {
    protected SnapshotProjectionsModule(ISnapshotProjectionsStore store, StreamId snapshotStreamId) {
        Store            = store;
        SnapshotStreamId = snapshotStreamId;
    }

    ISnapshotProjectionsStore Store            { get; }
    StreamId                  SnapshotStreamId { get; }

    protected readonly TSnapshot EmptySnapshot = new TSnapshot();

    Func<EventStoreRecord, DateTimeOffset> GetMessageTimestamp { get; set; } = record => record.Timestamp.ToUniversalTime();

    protected void SetMessageTimestampProvider(Func<EventStoreRecord, DateTimeOffset> getTimestamp) =>
        GetMessageTimestamp = getTimestamp;

    protected void UpdateWhen<T>(UpdateSnapshot<TSnapshot, T> update, Func<T, DateTimeOffset>? getTimestamp = null) where T : class, new() {
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
                    update(snapshot, msg);
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

    // protected void UpdateWhen<T>(UpdateSnapshotWithContext<TSnapshot, T> update, Func<T, Timestamp>? getTimestamp = null) {
    //     if (getTimestamp is null) {
    //         CheckProperty<T, Timestamp>("Timestamp");
    //         getTimestamp = evt => evt!.As<dynamic>().Timestamp;
    //     }
    //
    //     Process<T>(async (evt, ctx) => {
    //         try {
    //             var (snapshot, position) = await Store.LoadSnapshot<TSnapshot>(SnapshotStreamId);
    //
    //             snapshot.As<dynamic>().Metadata ??= new SnapshotMetadata();
    //
    //             Timestamp eventTimestamp     = getTimestamp(evt);
    //             Timestamp snapshotUpdateTime = snapshot.As<dynamic>().Metadata.UpdateTime;
    //
    //             if (eventTimestamp <= snapshotUpdateTime) {
    //                 ctx.Logger.LogTrace("{SnapshotName} update ignored on older {EventName} event: {Event}",
    //                     typeof(TSnapshot).Name,
    //                     typeof(T).Name,
    //                     evt);
    //             }
    //
    //             try {
    //                 update(snapshot, evt, ctx);
    //             }
    //             catch (Exception ex) {
    //                 // TODO SS: not sure if we should fail to update the snapshot atm.
    //                 // throw new Exception($"Failed to update {typeof(TSnapshot).Name} snapshot with {typeof(T).Name} event", ex);
    //                 ctx.Logger.LogCritical(ex,
    //                     "{SnapshotName} update failed when {EventName} event handled by user: {Event}",
    //                     typeof(TSnapshot).Name, typeof(T).Name, evt);
    //             }
    //
    //             // easy wait to skip updating the snapshot
    //             if (snapshot == EmptySnapshot)
    //                 return;
    //
    //             await Store.SaveSnapshot(SnapshotStreamId, position.StreamRevision, snapshot);
    //
    //             if (ctx.Logger.IsEnabled(LogLevel.Debug))
    //                 ctx.Logger.LogTrace(
    //                     "{SnapshotName} updated when {EventName} event: {Event}",
    //                     typeof(TSnapshot).Name, typeof(T).Name, evt);
    //         }
    //         catch (Exception ex) {
    //             ctx.Logger.LogError(ex,
    //                 "{SnapshotName} update failed when {EventName} event: {Event}",
    //                 typeof(TSnapshot).Name, typeof(T).Name, evt);
    //         }
    //     });
    // }

    // static void CheckProperty<TObject, TProperty>(string propertyName) {
    //     var property = typeof(TObject).GetProperty(propertyName);
    //
    //     if (property is null || property.PropertyType != typeof(TProperty))
    //         throw new InvalidOperationException($"{typeof(TObject).Name} must contain a '{propertyName}' property of type '{typeof(TProperty).Name}'");
    // }
}

public interface ISnapshotProjectionsStore {
    Task<(TSnapshot Snapshot, RecordPosition Position, DateTimeOffset Timestamp)> LoadSnapshot<TSnapshot>(StreamId snapshotStreamId) where TSnapshot : class, new();
    Task SaveSnapshot<TSnapshot>(StreamId snapshotStreamId, StreamRevision expectedRevision, DateTimeOffset Timestamp, TSnapshot snapshot) where TSnapshot : class, new();
}

public class SystemSnapshotProjectionsStore(
    Func<SystemReaderBuilder> getReaderBuilder,
    Func<SystemProducerBuilder> getProducerBuilder,
    TimeProvider? time = null
) : ISnapshotProjectionsStore {
    SystemReader   Reader   { get; } = getReaderBuilder().ReaderId("SystemSnapshotProjectionsStoreReader").Create();
    SystemProducer Producer { get; } = getProducerBuilder().ProducerId("SystemSnapshotProjectionsStoreProducer").Create();
    TimeProvider   Time     { get; } = time ?? TimeProvider.System;

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
            // .Message(snapshot.WithSnapshotMetadata(timestamp, expectedRevision + 1))
            .Message(snapshot)
            .Headers(headers => headers[SnapshotTimestampHeaderKey] = timestamp.ToIso8601())
            .Stream(snapshotStreamId)
            .ExpectedStreamRevision(expectedRevision)
            .Create();

        try {
            await Producer.Produce(produceRequest, throwOnError: true);
        }
        catch (Exception ex) {
            throw new Exception($"Unable to save snapshot to stream {snapshotStreamId} with expected revision {expectedRevision}", ex);
        }
    }
}

[PublicAPI]
static class MessageExtensions {
    // public static T WithUpdateTime<T>(this T message, Timestamp updateTime, bool strict = false) where T : IMessage {
    //     try {
    //         message.As<dynamic>().Metadata ??= new SnapshotMetadata();
    //         message.As<dynamic>().Metadata.UpdateTime = updateTime;
    //     }
    //     catch (Exception ex) when (strict) {
    //         throw new Exception($"Failed to set {typeof(T).Name}.UpdateTime={updateTime.ToDateTimeOffset():O}", ex);
    //     }
    //
    //     return message;
    // }
    //
    // public static T WithUpdateTime<T>(this T message, DateTimeOffset updateTime, bool strict = false) where T : IMessage =>
    //     message.WithUpdateTime(updateTime.ToTimestamp(), strict);
    //
    // public static T WithUpdateTime<T>(this T message, DateTime updateTime, bool strict = false) where T : IMessage =>
    //     message.WithUpdateTime(updateTime.ToTimestamp(), strict);
    //
    // public static T WithRevision<T>(this T message, long revision, bool strict = false) where T : IMessage {
    //     try {
    //         message.As<dynamic>().Metadata ??= new SnapshotMetadata();
    //         message.As<dynamic>().Metadata.Revision = revision;
    //     }
    //     catch (Exception ex) when (strict) {
    //         throw new Exception($"Failed to set {typeof(T).Name}.Revision={revision}", ex);
    //     }
    //
    //     return message;
    // }

    // internal static T WithSnapshotMetadata<T>(this T snapshotMessage, DateTimeOffset updateTime, long revision) where T : class {
    //     var newMetadata = new SnapshotMetadata {
    //         Revision   = revision,
    //         UpdateTime = updateTime.ToTimestamp(),
    //     };
    //
    //     try {
    //         snapshotMessage.As<dynamic>().Metadata = newMetadata;
    //     }
    //     catch (Exception ex) {
    //         throw new Exception($"Failed to update {typeof(T).Name} metadata: {newMetadata}", ex);
    //     }
    //
    //     return snapshotMessage;
    // }
}