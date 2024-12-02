// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DotNext;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Checkpoints;
using EventStore.Streaming.Consumers.Interceptors;
using EventStore.Streaming.Consumers.LifecycleEvents;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Streaming.JsonPath;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;
using Polly;

namespace EventStore.Connect.Consumers;

[PublicAPI]
public class SystemConsumer : IConsumer {
	public static SystemConsumerBuilder Builder => new();

	public SystemConsumer(SystemConsumerOptions options) {
		Options = string.IsNullOrWhiteSpace(options.Logging.LogName)
			? options with { Logging = options.Logging with { LogName = GetType().FullName! } }
			: options;

		var logger = Options.Logging.LoggerFactory.CreateLogger(GetType().FullName!);

		Client  = options.Publisher;

        Deserialize = Options.SkipDecoding
            ? (_, _) => ValueTask.FromResult<object?>(null)
            : (data, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Deserialize(data, headers);

		InboundChannel = Channel.CreateBounded<ResolvedEvent>(
			new BoundedChannelOptions(options.MessagePrefetchCount) {
				FullMode     = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			}
		);

		Sequence = new SequenceIdGenerator();

		if (options.Logging.Enabled)
			options.Interceptors.TryAddUniqueFirst(new ConsumerLogger());

        Interceptors = new(Options.Interceptors, logger);

		Intercept = evt => Interceptors.Intercept(evt);

        CheckpointStore = new CheckpointStore(
            Options.ConsumerId,
            SystemProducer.Builder.Publisher(Options.Publisher).ProducerId(Options.ConsumerId).Create(),
            SystemReader.Builder.Publisher(Options.Publisher).ReaderId(Options.ConsumerId).Create(),
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

    IPublisher                         Client               { get; }
    ResiliencePipeline                 ResiliencePipeline   { get; }
    Deserialize                        Deserialize          { get; }
    CheckpointController               CheckpointController { get; }
    ICheckpointStore                   CheckpointStore      { get; }
    Channel<ResolvedEvent>             InboundChannel       { get; }
    SequenceIdGenerator                Sequence             { get; }
	InterceptorController              Interceptors         { get; }
	Func<ConsumerLifecycleEvent, Task> Intercept            { get; }

	public string                        ConsumerId       => Options.ConsumerId;
    public string                        ClientId         => Options.ClientId;
    public string                        SubscriptionName => Options.SubscriptionName;
    public ConsumeFilter                 Filter           => Options.Filter;

    // public IReadOnlyList<RecordPosition> TrackedPositions => []; //CheckpointController.Positions;

    public RecordPosition StartPosition        { get; private set; }
    public RecordPosition LastCommitedPosition { get; private set; }

	CancellationTokenSource Cancellator { get; set; } = new();

    public async IAsyncEnumerable<EventStoreRecord> Records([EnumeratorCancellation] CancellationToken stoppingToken) {
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

		await CheckpointStore.Initialize(Cancellator.Token);

        StartPosition = await CheckpointStore
            .ResolveStartPosition(Options.StartPosition, Options.InitialPosition, Cancellator.Token);

        if (Options.Filter.IsStreamIdFilter) {
            var startRevision = await Client.GetStreamRevision(StartPosition.ToPosition(), stoppingToken);

            await Client.SubscribeToStream(startRevision,
                Options.Filter.Expression,
                InboundChannel,
                ResiliencePipeline,
                Cancellator.Token);
        } else {
            await Client.SubscribeToAll(StartPosition.ToPosition(),
                Options.Filter.ToEventFilter(),
                InboundChannel,
                ResiliencePipeline,
                Cancellator.Token);
        }

		await CheckpointController.Activate();

		await foreach (var resolvedEvent in InboundChannel.Reader.ReadAllAsync(CancellationToken.None)) {
			if (Cancellator.IsCancellationRequested)
				yield break; // get out regardless of the number of events still in the channel

			var record = await resolvedEvent.ToRecord(Deserialize, Sequence.FetchNext);

			if (record == EventStoreRecord.None)
				continue;

            if (Options.Filter.IsJsonPathFilter && !Options.Filter.JsonPath.IsMatch(record))
                continue;

			await Intercept(new RecordReceived(this, record));
			yield return record;
		}
	}

    public async Task<IReadOnlyList<RecordPosition>> Track(EventStoreRecord record, CancellationToken cancellationToken = default) {
        var trackedPositions = await CheckpointController.Track(record);
        await Intercept(new RecordTracked(this, record));
        return trackedPositions;
    }

    public Task<IReadOnlyList<RecordPosition>> Commit(EventStoreRecord record, CancellationToken cancellationToken = default) =>
        CheckpointController.Commit(record);

    /// <summary>
    /// Commits all tracked positions that are ready to be committed (complete sequences).
    /// </summary>
    public async Task<IReadOnlyList<RecordPosition>> CommitAll(CancellationToken cancellationToken = default) =>
        await CheckpointController.CommitAll();

    public async Task<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = default) =>
		await CheckpointStore.LoadPositions(cancellationToken);

    public async ValueTask DisposeAsync() {
        try {
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