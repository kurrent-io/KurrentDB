// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.JsonPath;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Connect.Consumers;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using Polly;
using StreamRevision = Kurrent.Surge.StreamRevision;

namespace KurrentDB.Surge.Readers;

[PublicAPI]
public class SystemReader : IReader {
	public static SystemReaderBuilder Builder => new();

	public SystemReader(SystemReaderOptions options) {
        Options = options;
        Client  = options.Client;

		Deserialize = Options.SkipDecoding
			? (_, _) => ValueTask.FromResult<object?>(null)
			: (data, headers) => options.SchemaRegistry.As<ISchemaSerializer>().Deserialize(data, headers);

        // if (options.EnableLogging)
        //     options.Interceptors.TryAddUniqueFirst(new ReaderLogger(nameof(SystemReader)));
        //
        // Interceptors = new(Options.Interceptors, Options.LoggerFactory.CreateLogger(nameof(SystemReader)));
        //
        // Intercept = evt => Interceptors.Intercept(evt);

        ResiliencePipeline = options.ResiliencePipelineBuilder
            .With(x => x.InstanceName = "SystemReaderResiliencePipeline")
            .Build();
	}

    internal SystemReaderOptions Options { get; }

    ISystemClient      Client             { get; }
    ResiliencePipeline ResiliencePipeline { get; }
    Deserialize        Deserialize        { get; }

    // InterceptorController              Interceptors       { get; }
    // Func<ConsumerLifecycleEvent, Task> Intercept          { get; }

    public string ReaderId => Options.ReaderId;

    public async IAsyncEnumerable<SurgeRecord> Read(
        LogPosition position, ReadDirection direction,
        ConsumeFilter filter, int maxCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var sequence = new SequenceIdGenerator();

        var startPosition = position == LogPosition.Earliest
            ? Position.Start
            : new(
                position.CommitPosition.GetValueOrDefault(),
                position.PreparePosition.GetValueOrDefault()
            );

        var readForwards = direction == ReadDirection.Forwards;

        IAsyncEnumerable<ResolvedEvent> events;

        if (filter.IsStreamIdFilter) {
            var startRevision = await Client.Management.GetStreamRevision(startPosition, cancellator.Token);

            events = Client.Reading.ReadStream(
                filter.Expression,
                startRevision,
                maxCount,
                readForwards,
                cancellator.Token
            );
        }
        else {
            events = Client.Reading.Read(
                startPosition,
                ConsumeFilterExtensions.ToEventFilter(filter),
                maxCount,
                readForwards,
                cancellator.Token
            );
        }

        // but how since we are using IAsyncEnumerable, pipes?
        // try {
        //
        //
        // }
        // catch (Exception ex) {
        //     StreamingError error = ex switch {
        //         ReadResponseException.Timeout when filter.IsStreamIdFilter        => new RequestTimeoutError(filter.Expression, ex.Message),
        //         ReadResponseException.StreamNotFound when filter.IsStreamIdFilter => new StreamNotFoundError(filter.Expression),
        //         ReadResponseException.StreamDeleted when filter.IsStreamIdFilter  => new StreamDeletedError(filter.Expression),
        //         ReadResponseException.AccessDenied when filter.IsStreamIdFilter   => new StreamAccessDeniedError(filter.Expression),
        //
        //         ReadResponseException.NotHandled.ServerNotReady => new ServerNotReadyError(),
        //         ReadResponseException.NotHandled.ServerBusy     => new ServerTooBusyError(),
        //         ReadResponseException.NotHandled.LeaderInfo li  => new ServerNotLeaderError(li.Host, li.Port),
        //         ReadResponseException.NotHandled.NoLeaderInfo   => new ServerNotLeaderError(),
        //         _                                               => new StreamingCriticalError(ex.Message, ex)
        //     };
        //
        //     throw error;
        // }

        await foreach (var re in events) {
            if (cancellator.IsCancellationRequested)
                yield break;

            var record = await re.ToRecord(Deserialize, sequence.FetchNext);

            if (filter.IsJsonPathFilter && !filter.JsonPath.IsMatch(record))
                continue;

            yield return record;
        }
    }

    public IAsyncEnumerable<SurgeRecord> Read(StreamId streamId, StreamRevision revision, ReadDirection direction, int maxCount, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public async ValueTask<SurgeRecord> ReadLastStreamRecord(StreamId stream, CancellationToken cancellationToken = default) {
        try {
            var result = await Client.Reading.ReadStreamLastEvent(stream, cancellationToken);

            return result is not null
                ? await result.Value.ToRecord(Deserialize, () => SequenceId.From(1))
                : SurgeRecord.None;
        } catch (ReadResponseException.StreamNotFound) {
            return SurgeRecord.None;
        }
    }

    public async ValueTask<SurgeRecord> ReadFirstStreamRecord(StreamId stream, CancellationToken cancellationToken = default) {
        try {
            var result = await Client.Reading.ReadStreamFirstEvent(stream, cancellationToken);

            return result is not null
                ? await result.Value.ToRecord(Deserialize, () => SequenceId.From(1))
                : SurgeRecord.None;
        } catch (ReadResponseException.StreamNotFound) {
            return SurgeRecord.None;
        }
    }

    public async ValueTask<SurgeRecord> ReadRecord(LogPosition position, CancellationToken cancellationToken = default) {
        try {
            var esdbPosition = position == LogPosition.Earliest
                ? Position.Start
                : new(
                    position.CommitPosition.GetValueOrDefault(),
                    position.PreparePosition.GetValueOrDefault()
                );

            var result = await Client.Reading.ReadEvent(esdbPosition, cancellationToken);

            return !result.Equals(ResolvedEvent.EmptyEvent)
                ? await result.ToRecord(Deserialize, () => SequenceId.From(1))
                : SurgeRecord.None;
        } catch (ReadResponseException.StreamNotFound) {
            return SurgeRecord.None;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
