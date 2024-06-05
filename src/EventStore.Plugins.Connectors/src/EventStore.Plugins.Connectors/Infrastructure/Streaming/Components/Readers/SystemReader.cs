// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.LifecycleEvents;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Readers.Configuration;
using EventStore.Streaming.Schema.Serializers;
using Polly;

namespace EventStore.Streaming.Readers;

[PublicAPI]
public class SystemReader : IReader {
	public static SystemReaderBuilder Builder => new();

	public SystemReader(SystemReaderOptions options) {
        Options = options;
        Client  = options.Publisher;

		Deserialize = Options.SkipDecoding
			? (_, _) => ValueTask.FromResult<object?>(null)
			: (data, headers) => options.GetSchemaRegistry().As<ISchemaSerializer>().Deserialize(data, headers);

		Sequence = new SequenceIdGenerator();

        // if (options.EnableLogging)
        //     options.Interceptors.TryAddUniqueFirst(new ReaderLogger(nameof(SystemReader)));
        //
        // Interceptors = new(Options.Interceptors, Options.LoggerFactory.CreateLogger(nameof(SystemReader)));
        //
        // Intercept = evt => Interceptors.Intercept(evt);

        ResiliencePipeline = options.ResiliencePipelineBuilder
            .With(x => x.InstanceName = "SystemReaderPipeline")
            .Build();
	}

    internal SystemReaderOptions Options { get; }

    IPublisher                         Client             { get; }
    ResiliencePipeline                 ResiliencePipeline { get; }
    Deserialize                        Deserialize        { get; }
    SequenceIdGenerator                Sequence           { get; }
    InterceptorController              Interceptors       { get; }
    Func<ConsumerLifecycleEvent, Task> Intercept          { get; }

    public string ReaderName => Options.ReaderName;

	CancellationTokenSource Cancellator  { get; set; } = null!;

    public async IAsyncEnumerable<EventStoreRecord> Read(
        LogPosition position, ReadDirection direction,
        ConsumeFilter filter, int maxCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        Cancellator = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var startPosition = position == LogPosition.Earliest
            ? Position.Start
            : new(
                position.CommitPosition.GetValueOrDefault(),
                position.PreparePosition.GetValueOrDefault()
            );

        var readForwards = direction == ReadDirection.Forwards;

        IAsyncEnumerable<ResolvedEvent> events;

        // If the filter applies only to a single stream we can optimize the read
        if (filter is { IsStreamFilter: true, Prefixes.Length: 1 }) {
            var startRevision = await Client.GetStreamRevision(startPosition, Cancellator.Token);

            events = Client.ReadStream(
                filter.Prefixes[0],
                startRevision,
                maxCount,
                readForwards,
                Cancellator.Token
            );
        }
        else {
            events = Client.Read(
                startPosition,
                filter.ToEventFilter(),
                maxCount,
                readForwards,
                Cancellator.Token
            );
        }

        await foreach (var re in events) {
            if (Cancellator.IsCancellationRequested)
                yield break;

            yield return await re.ToRecord(Deserialize, Sequence.FetchNext);
        }
    }

    public async ValueTask DisposeAsync() {
		await Cancellator.CancelAsync();
		Cancellator.Dispose();
	}
}