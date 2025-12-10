// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DotNext;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Checkpoints;
using Kurrent.Surge.Consumers.Interceptors;
using Kurrent.Surge.Consumers.LifecycleEvents;
using Kurrent.Surge.Interceptors;
using Kurrent.Surge.JsonPath;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Connect.Consumers;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.Logging;
using Polly;

namespace KurrentDB.Surge.Consumers;

[PublicAPI]
public class SystemConsumer : IConsumer {
	public static SystemConsumerBuilder Builder => new();

	public SystemConsumer(SystemConsumerOptions options) {
		Options = string.IsNullOrWhiteSpace(options.Logging.LogName)
			? options with { Logging = options.Logging with { LogName = GetType().FullName! } }
			: options;

		var logger = Options.Logging.LoggerFactory.CreateLogger(GetType().FullName!);

		Client  = options.Client;

        Deserialize = Options.SkipDecoding
            ? (_, _) => ValueTask.FromResult<object?>(null)
            : (data, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Deserialize(data, headers);

		InboundChannel = Channel.CreateBounded<ReadResponse>(
			new BoundedChannelOptions(options.MessagePrefetchCount) {
				FullMode     = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			}
		);

		Sequence = new SequenceIdGenerator();

        var interceptors = new LinkedList<InterceptorModule>(options.Interceptors);

		if (options.Logging.Enabled)
			interceptors.TryAddUniqueFirst(new ConsumerLogger());

        interceptors.TryAddUniqueFirst(new ConsumerMetrics());

        Interceptors = new(interceptors, logger);

		Intercept = evt => Interceptors.Intercept(evt);

        CheckpointStore = new CheckpointStore(
            Options.ConsumerId,
            SystemProducer.Builder.Client(Options.Client).ProducerId(Options.ConsumerId).Create(),
            SystemReader.Builder.Client(Options.Client).ReaderId(Options.ConsumerId).Create(),
            TimeProvider.System,
            options.AutoCommit.StreamTemplate.GetStream(Options.ConsumerId)
        );

        CheckpointController = new CheckpointController(
            async (positions, token) => {
                try {
                    if (positions.Count > 0) {
                        await CheckpointStore.CommitPositions(positions, token);
                        LastCommitedPosition = positions[^1];
                    }

                    await Intercept(new PositionsCommitted(this, positions));
                    return positions;
                }
                catch (Exception ex) {
                    await Intercept(new PositionsCommitError(this, positions, ex));
                    throw;
                }
            },
            Options.AutoCommit,
            Options.Logging.LoggerFactory.CreateLogger<CheckpointController>(),
            ConsumerId
        );

		ResiliencePipeline = options.ResiliencePipelineBuilder
			.With(x => x.InstanceName = "SystemConsumerPipeline")
			.Build();

        StartPosition = RecordPosition.Unset;
    }

	internal SystemConsumerOptions Options { get; }

    ISystemClient                           Client               { get; }
    ResiliencePipeline                      ResiliencePipeline   { get; }
    Deserialize                             Deserialize          { get; }
    CheckpointController                    CheckpointController { get; }
    ICheckpointStore                        CheckpointStore      { get; }
    Channel<ReadResponse>                   InboundChannel       { get; }
    SequenceIdGenerator                     Sequence             { get; }
	InterceptorController                   Interceptors         { get; }
	Func<ConsumerLifecycleEvent, ValueTask> Intercept            { get; }

	public string                        ConsumerId       => Options.ConsumerId;
    public string                        ClientId         => Options.ClientId;
    public string                        SubscriptionName => Options.SubscriptionName;
    public ConsumeFilter                 Filter           => Options.Filter;

    // public IReadOnlyList<RecordPosition> TrackedPositions => []; //CheckpointController.Positions;

    public RecordPosition StartPosition        { get; private set; }
    public RecordPosition LastCommitedPosition { get; private set; }

	CancellationTokenSource Cancellator { get; set; } = new();

    public async IAsyncEnumerable<SurgeRecord> Records([EnumeratorCancellation] CancellationToken stoppingToken) {
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

		var cancellatorToken = Cancellator.Token;

		await CheckpointStore.Initialize(Cancellator.Token);

		StartPosition = await CheckpointStore
			.ResolveStartPosition(Options.StartPosition, Options.InitialPosition, cancellatorToken)
			;

		if (Options.Filter.IsStreamIdFilter) {
            var startRevision = await Client.Management.GetStreamRevision(StartPosition.ToPosition() ?? Position.Start, stoppingToken);

            await Client.Subscriptions.SubscribeToStream(
	            startRevision,
                Options.Filter.Expression,
                InboundChannel,
                ResiliencePipeline,
                cancellatorToken
	        );
        } else {
            await Client.Subscriptions.SubscribeToAll(
	            StartPosition.ToPosition(),
                Options.Filter.ToEventFilter(),
	            (uint)Options.AutoCommit.RecordsThreshold,
                InboundChannel,
                ResiliencePipeline,
	            cancellatorToken
	        );
        }

		await CheckpointController.Activate();

		var lastReadRecord = SurgeRecord.None;

		await foreach (var response in InboundChannel.Reader.ReadAllAsync(CancellationToken.None)) {
			if (cancellatorToken.IsCancellationRequested)
				yield break; // get out regardless of the number of events still in the channel

			if (response is ReadResponse.EventReceived eventReceived) {
				var resolvedEvent = eventReceived.Event;

				lastReadRecord = await resolvedEvent.ToRecord(Deserialize, Sequence.FetchNext);

                // TODO WC: To be reviewed. We should be able to delete this because it should never happen
				if (lastReadRecord == SurgeRecord.None)
					continue;

				if (Options.Filter.IsJsonPathFilter && !Options.Filter.JsonPath.IsMatch(lastReadRecord))
					continue;

				await Intercept(new RecordReceived(this, lastReadRecord));

				yield return lastReadRecord;
			}
			else if (response is ReadResponse.CheckpointReceived checkpointReceived) {
				lastReadRecord = new SurgeRecord {
					Id         = RecordId.From(Guid.NewGuid()),
					Position   = LogPosition.From(checkpointReceived.CommitPosition, checkpointReceived.PreparePosition != 0 ? checkpointReceived.PreparePosition : checkpointReceived.CommitPosition),
					SequenceId = Sequence.FetchNext(),
					Timestamp  = TimeProvider.System.GetUtcNow().DateTime,
					ValueType  = typeof(ReadResponse.CheckpointReceived),
					Value      = checkpointReceived,
					SchemaInfo = new SchemaInfo("$checkpoint-received", SchemaDataFormat.Json)
				};

				await Intercept(new RecordReceived(this, lastReadRecord));

				await CheckpointController.Track(lastReadRecord);
				await Intercept(new RecordTracked(this, lastReadRecord));

				yield return lastReadRecord;
			}
			else if (response is ReadResponse.SubscriptionCaughtUp caughtUp) {
				var record = new SurgeRecord {
					Id         = RecordId.From(Guid.NewGuid()),
					Position   = lastReadRecord.Position,
					SequenceId = lastReadRecord.SequenceId,
					Timestamp  = TimeProvider.System.GetUtcNow().DateTime,
					ValueType  = typeof(ReadResponse.SubscriptionCaughtUp),
					Value      = caughtUp,
					SchemaInfo = new SchemaInfo("$subscription-caughtUp", SchemaDataFormat.Json)
				};

				await Intercept(new RecordReceived(this, record));

				// it is not required to track this record because the CheckpointReceived event
				// is the one that actually gets tracked.
				if (Options.AutoCommit.Enabled)
					await CheckpointController.CommitAll();

				yield return record;
			}
		}
    }

    public async ValueTask<IReadOnlyList<RecordPosition>> Track(SurgeRecord record, CancellationToken cancellationToken = default) {
	    if (record.Value is ReadResponse.CheckpointReceived or ReadResponse.SubscriptionCaughtUp)
		    return CheckpointController.TrackedPositions;

	    var trackedPositions = await CheckpointController.Track(record);
        await Intercept(new RecordTracked(this, record));
        return trackedPositions;
    }

    public async ValueTask<IReadOnlyList<RecordPosition>> Commit(SurgeRecord record, CancellationToken cancellationToken = default) {
	    if (record.Value is ReadResponse.CheckpointReceived or ReadResponse.SubscriptionCaughtUp)
		    return CheckpointController.TrackedPositions;

	    var trackedPositions = await CheckpointController.Commit(record);
	    await Intercept(new RecordTracked(this, record));
	    return trackedPositions;
    }

    /// <summary>
    /// Commits all tracked positions that are ready to be committed (complete sequences).
    /// </summary>
    public async ValueTask<IReadOnlyList<RecordPosition>> CommitAll(CancellationToken cancellationToken = default) =>
        await CheckpointController.CommitAll();

    public async ValueTask<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = default) =>
		await CheckpointStore.LoadPositions(cancellationToken);

    public async ValueTask DisposeAsync() {
        try {
			// stops the subscription if it was not already stopped
	        if (!Cancellator.IsCancellationRequested)
                await Cancellator.CancelAsync();

            // stops the periodic commit if it was not already stopped
            // we might not need to implement this if we can guarantee that
            await CheckpointController.DisposeAsync();

            Cancellator.Dispose();

            await Intercept(new ConsumerStopped(this));
        }
        catch (Exception ex) {
            await Intercept(new ConsumerStopped(this, ex));
            throw;
        }
        finally {
            await Interceptors.DisposeAsync();
        }
    }
}
