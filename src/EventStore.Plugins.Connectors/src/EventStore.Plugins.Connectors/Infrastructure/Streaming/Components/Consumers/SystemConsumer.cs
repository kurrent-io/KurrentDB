// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming.Consumers.Checkpoints;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Consumers.Interceptors;
using EventStore.Streaming.Consumers.LifecycleEvents;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Schema.Serializers;
using Polly;

namespace EventStore.Streaming.Consumers;

[PublicAPI]
public class SystemConsumer : IConsumer {
	public static SystemConsumerBuilder Builder => new();

	public SystemConsumer(SystemConsumerOptions options) {
		Options = options;
		Client  = options.Publisher;

        Deserialize = Options.SkipDecoding
            ? (_, _) => ValueTask.FromResult<object?>(null)
            : (data, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Deserialize(data, headers);

        CheckpointStore = new(Client, options.SubscriptionName, options.ConsumerName);

		CheckpointController = new(
			new CheckpointControllerOptions {
				AutoCommit      = options.AutoCommit,
				CommitPositions = (positions, _) => Commit(positions),
				LoggerFactory   = options.LoggerFactory
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

		if (options.EnableLogging)
			options.Interceptors.TryAddUniqueFirst(new ConsumerLogger(nameof(SystemConsumer)));

        Interceptors = new(Options.Interceptors, Options.LoggerFactory.CreateLogger(nameof(SystemConsumer)));

		Intercept = evt => Interceptors.Intercept(evt);

		ResiliencePipeline = options.ResiliencePipelineBuilder
			.With(x => x.InstanceName = "SystemConsumerPipeline")
			.Build();
	}

	internal SystemConsumerOptions Options { get; }

	IPublisher                         Client               { get; }
	ResiliencePipeline                 ResiliencePipeline   { get; }
	Deserialize                        Deserialize          { get; }
	CheckpointController               CheckpointController { get; }
	SystemCheckpointStore              CheckpointStore      { get; }
	Channel<ResolvedEvent>             InboundChannel       { get; }
	SequenceIdGenerator                Sequence             { get; }
	InterceptorController              Interceptors         { get; }
	Func<ConsumerLifecycleEvent, Task> Intercept            { get; }

	public string        ConsumerName     => Options.ConsumerName;
	public string        SubscriptionName => Options.SubscriptionName;
	public string[]      Streams          => Options.Streams;
	public ConsumeFilter Filter           => Options.Filter;

	CancellationTokenSource Cancellator { get; set; } = null!;

    public async IAsyncEnumerable<EventStoreRecord> Records([EnumeratorCancellation] CancellationToken stoppingToken) {
		// create a new cancellator to be used in case
		// we cancel the subscription on dispose.
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

		// ensure the positions stream exists and is correctly configured
		await CheckpointStore.Initialize(Cancellator.Token);

		// not sure about this still... use log position or record position?
		// if it's a stream sub then it should be the stream revision inside the record position...
		var startPosition = await CheckpointStore.ResolveStartPosition(
			Options.InitialPosition,
			Options.StartPosition,
			Cancellator.Token
		);

		await Client.SubscribeToAll(
			startPosition.ToPosition(),
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

			var record = await resolvedEvent.ToRecord(Deserialize, Sequence.FetchNext);
			await Intercept(new RecordReceived(this, record));
			yield return record;
		}

		// if we got here it's because the cancellation was requested
		await Intercept(new ConsumerUnsubscribed(this));
	}

	public async Task Track(EventStoreRecord record) {
		await CheckpointController.Track(record);
		await Intercept(new RecordTracked(this, record));
	}

	public async Task Commit() {
		var positions = await CheckpointController.Commit(CancellationToken.None);
		await Intercept(new PositionsCommitted(this, positions));
	}

	public async Task Commit(params RecordPosition[] positions) {
		try {
			await CheckpointStore.CommitPositions(positions);
			await Intercept(new PositionsCommitted(this, positions));
		}
		catch (Exception ex) {
			await Intercept(new PositionsCommitError(this, positions, ex));
			throw;
		}
	}

	public async Task<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = default) =>
		await CheckpointStore.GetLatestPositions(cancellationToken);

    public async Task Unsubscribe() {
        try {
            if (!Cancellator.IsCancellationRequested)
                await Cancellator.CancelAsync();

            // stops the periodic commit
            await CheckpointController.Deactivate();

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

	public async ValueTask DisposeAsync() =>
        await Unsubscribe();
}