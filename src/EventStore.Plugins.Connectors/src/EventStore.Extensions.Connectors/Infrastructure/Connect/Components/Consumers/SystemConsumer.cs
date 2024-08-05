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
using EventStore.Streaming.Transformers;
using Polly;

namespace EventStore.Connect.Consumers;

[PublicAPI]
public class SystemConsumer : IConsumer {
	public static SystemConsumerBuilder Builder => new();

	public SystemConsumer(SystemConsumerOptions options) {
		Options = options;
		Client  = options.Publisher;

		Transformer = options.Transformer;

        Deserialize = Options.SkipDecoding
            ? (_, _) => ValueTask.FromResult<object?>(null)
            : (data, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Deserialize(data, headers);

        CheckpointStore = new CheckpointStore(
            Options.ConsumerId,
            SystemProducer.Builder.Publisher(Options.Publisher).ProducerId(Options.ConsumerId).Create(),
            SystemReader.Builder.Publisher(Options.Publisher).ReaderId(Options.ConsumerId).Create(),
            TimeProvider.System,
            options.AutoCommit.StreamTemplate.GetStream(Options.ConsumerId)
        );

		CheckpointController = new(
			new CheckpointControllerOptions {
				AutoCommit      = options.AutoCommit,
				CommitPositions = (positions, _) => Commit(positions, CancellationToken.None),
				LoggerFactory   = options.Logging.LoggerFactory
			}
		);

		InboundChannel = Channel.CreateBounded<ResolvedEvent>(
			new BoundedChannelOptions(options.MessagePrefetchCount) {
				FullMode     = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			}
		);

		Sequence = new SequenceIdGenerator();

		if (options.Logging.Enabled)
			options.Interceptors.TryAddUniqueFirst(new ConsumerLogger(nameof(SystemConsumer)));

        Interceptors = new(Options.Interceptors, Options.Logging.LoggerFactory.CreateLogger(nameof(SystemConsumer)));

		Intercept = evt => Interceptors.Intercept(evt);

		ResiliencePipeline = options.ResiliencePipelineBuilder
			.With(x => x.InstanceName = "SystemConsumerResiliencePipeline")
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
	ITransformer?                      Transformer          { get; }

	public string                        ConsumerId       => Options.ConsumerId;
    public string                        ClientId         => Options.ClientId;
    public string                        SubscriptionName => Options.SubscriptionName;
    public ConsumeFilter                 Filter           => Options.Filter;
    public IReadOnlyList<RecordPosition> TrackedPositions => []; //CheckpointController.Positions;

    public RecordPosition StartPosition        { get; private set; }
    public RecordPosition LastCommitedPosition { get; private set; }

	CancellationTokenSource Cancellator { get; set; } = new();

    public async IAsyncEnumerable<EventStoreRecord> Records([EnumeratorCancellation] CancellationToken stoppingToken) {
		// create a new cancellator to be used in case
		// we cancel the subscription on dispose.
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

		// ensure the positions stream exists and is correctly configured
		await CheckpointStore.Initialize(Cancellator.Token);

		StartPosition = await CheckpointStore
            .ResolveStartPosition(Options.StartPosition, Options.InitialPosition, Cancellator.Token);

		await Client.SubscribeToAll(
            StartPosition.ToPosition(),
            Options.Filter.ToEventFilter(),
            InboundChannel,
            ResiliencePipeline,
            Cancellator.Token
        );

		// we must be able to stop the consumer and still
		// commit the remaining positions if any, hence
		// why we don't pass the cancellation token here
		await CheckpointController.Activate(CancellationToken.None);

		await foreach (var resolvedEvent in InboundChannel.Reader.ReadAllAsync(CancellationToken.None)) {
			if (Cancellator.IsCancellationRequested)
				yield break; // get out regardless of the number of events still in the channel

			var record = await (
				Transformer is not null
					? resolvedEvent.ToTransformedRecord(Transformer, Sequence.FetchNext)
					: resolvedEvent.ToRecord(Deserialize, Sequence.FetchNext)
			);

			if (record == EventStoreRecord.None)
				continue;

            if (Options.Filter.IsJsonPathFilter && !Options.Filter.JsonPath.IsMatch(record))
                continue;

			await Intercept(new RecordReceived(this, record));
			yield return record;
		}
	}

    public async Task Track(EventStoreRecord record) {
        await CheckpointController.Track(record);
        await Intercept(new RecordTracked(this, record));
    }

    public async Task Commit(CancellationToken cancellationToken) {
        _ = await CheckpointController.Commit(cancellationToken);
    }

    async Task Commit(RecordPosition[] positions, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        try {
            if (!positions.IsNullOrEmpty()) {
                await CheckpointStore.CommitPositions(positions, cancellationToken);
                LastCommitedPosition = positions[^1];
            }

            await Intercept(new PositionsCommitted(this, positions));
        }
        catch (Exception ex) {
            await Intercept(new PositionsCommitError(this, positions, ex));
            throw;
        }
    }

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