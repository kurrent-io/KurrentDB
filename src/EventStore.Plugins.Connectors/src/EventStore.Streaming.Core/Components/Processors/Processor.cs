#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Interceptors;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Processors.Interceptors;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Routing;
using EventStore.Streaming.Schema;
using static System.Threading.Tasks.TaskCreationOptions;

namespace EventStore.Streaming.Processors;

[PublicAPI]
public class Processor<TOptions> : IProcessor where TOptions : ProcessorOptions<TOptions>  {
	public Processor(TOptions options) {
        ProcessorId      = options.ProcessorId;
		ProcessorName    = options.ProcessorName;
		ProcessorType    = GetType();
		SubscriptionName = options.SubscriptionName;
		Streams          = options.Streams;
		Filter           = options.Filter;
		Logger           = options.LoggerFactory.CreateLogger(ProcessorType.Name);
		
		// processor state and positions should go into the same subscription group stream
		// not sure about naming conventions here, but we should have a way to identify the processor
		// and the subscription group it is part of
		
		//var subscriptionKey         = UniversalUniqueIdentifiers.GenerateFrom(options.SubscriptionName).ToString("N");
		
		//var stateStream = $"$consumer-state-{options.SubscriptionName}";
		var stateStream = $"$consumer-group-{options.SubscriptionName}";
		//var stateStream = $"$consumer-positions-{options.SubscriptionName}";
		
		var processorLogName = ProcessorType.Name;
		
		if (options.EnableLogging) {
			//options.Interceptors.TryAddUniqueFirst(new ProducerLogger(processorLogName)); 
			options.Interceptors.TryAddUniqueFirst(new ConsumerLogger(processorLogName));
			options.Interceptors.TryAddUniqueFirst(new ProcessorLogger(processorLogName));
		}
		
		options.Interceptors.TryAddUniqueFirst(
			new ProcessorStateChangePublisher(
				options.GetProducer(
					options with { 
						ProcessorName       = $"{options.ProcessorName}-state-publisher",
						DefaultOutputStream = stateStream
					}
				),
				processorLogName
			)
		);

		SchemaRegistry = options.SchemaRegistry;
		StateStore     = options.StateStore;

		Consumer = options.GetConsumer(options);
		Producer = options.GetProducer(options);

		Streams = Consumer.Streams;
		Filter  = Consumer.Filter;
        
        Router = new MessageRouter(options.RouterRegistry);
        
		// Router = options.Modules.Aggregate(
		// 	new MessageRouter(), 
		// 	static (router, module) => module.RegisterHandlersOn(router)
		// );
		
		// makes no sense at all to iterate over all the modules and call the handlers for nothing at all...
		// it works but yeah...it's not the best way to do it...
		Interceptors = new(options.Interceptors, Logger);

		Intercept = evt => Interceptors.Intercept(evt);

		State    = ProcessorState.Stopped;
		Suspended = new(true);

		Finisher = new(RunContinuationsAsynchronously);
		Stopped  = Finisher.Task;
	}

	SchemaRegistry               SchemaRegistry { get; }
	ILogger                      Logger         { get; }
	IConsumer                    Consumer       { get; }
	IProducer                    Producer       { get; }
	MessageRouter                Router         { get; }
	IStateStore                  StateStore     { get; }
	InterceptorController        Interceptors   { get; }
	Func<InterceptorEvent, Task> Intercept      { get; }
	ManualResetEventSlim         Suspended      { get; }
	TaskCompletionSource         Finisher       { get; } 
	
	CancellationTokenSource Cancellator { get; set; } = null!;

    public string        ProcessorId      { get; }
	public string        ProcessorName    { get; }
	public Type          ProcessorType    { get; }
	public string        SubscriptionName { get; }
	public string[]      Streams          { get; }
	public ConsumeFilter Filter           { get; }

	public ProcessorState State   { get; private set; }
	public Task           Stopped { get; }
	
	public IReadOnlyCollection<EndpointInfo> Endpoints => Router.Endpoints;
	
	async Task ChangeProcessorState(ProcessorState state, Exception? error = null) {
		var previousState = State;
		State = state;
		await Intercept(new ProcessorStateChanged(this, FromState: previousState, Error: error));
	}
	
	public async Task Activate(CancellationToken stoppingToken) {
		if (State is ProcessorState.Running)
			return;

		await ChangeProcessorState(ProcessorState.Activating);

		// There is room for improvement here, we should be able to
		// register the consumer, create whatever we require upfront
		// and right after actually start consuming records...
		
		// ensure stop when stopping token is cancelled
		stoppingToken.Register(() => {
			if (State 
			    is ProcessorState.Running 
			    or ProcessorState.Suspended 
			    or ProcessorState.Activating)
				_ = Deactivate(); 
		});
		
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		
		await ChangeProcessorState(ProcessorState.Running);

		_ = Task.Run(
			async () => {
				try {
					// TODO SS: we suspend the processing of records, but the consumer is still active and maybe reading from streams... fix this.
					await foreach (var record in Consumer.Records(Cancellator.Token)) {
						Suspended.Wait(Cancellator.Token);
						await ProcessRecord(record, Cancellator.Token);
					}
				}
				catch (OperationCanceledException) {
					// this is expected, we are stopping the processor
				}
				catch (Exception ex) {
					// any other exception should stop the processor if it is running
					// what errors could we catch here after we already
					// initiated the stopping process? does it even matter?
					if (State 
					    is ProcessorState.Running 
					    or ProcessorState.Suspended 
					    or ProcessorState.Activating)
						await Deactivate(ex);
				}
			},
			Cancellator.Token
		);
	}

	public async Task Suspend() {
		if (State is ProcessorState.Activating or ProcessorState.Running) {
			Suspended.Reset();
			await ChangeProcessorState(ProcessorState.Suspended);
		}
	}
	
	public async Task Resume() {
		if (State is ProcessorState.Suspended) {
			Suspended.Set();
			await ChangeProcessorState(ProcessorState.Running);
		}
	}

	public async Task Deactivate() {
		if (State is ProcessorState.Running or ProcessorState.Suspended)
			await Deactivate(null);
	}

	public async ValueTask DisposeAsync() => 
		await Deactivate();

	async Task ProcessRecord(EventStoreRecord record, CancellationToken cancellationToken) {
		if (!Router.CanProcessRecord(record)) {
			await Consumer.Track(record);
			await Intercept(new RecordSkipped(this, record));
			return;
		}

		await Intercept(new RecordReady(this, record));

		var context = new RecordContext(this, record, StateStore, Logger, cancellationToken);

		try {
			using (var _ = Logger.BeginPropertyScope(
				("RecordId", record.Id),
				("RecordSchemaSubject", record.SchemaInfo.Subject),
				("RecordPosition", record.Position)
			)){
				await Router.ProcessRecord(context);
			}
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (Exception ex) {
			await Intercept(new RecordHandlingError(this, record, ex));
			await Deactivate(ex);
			return;
		}
		
		var requests     = context.Requests();
		var requestCount = requests.Length;
		
		await Intercept(new RecordHandled(this, record, requests));
		
		if (requestCount == 0) {
			await Consumer.Track(record);
			await Intercept(new RecordProcessed(this, record));
			return;
		}

		var results = new List<SendResult>();

		// TODO SS: attempt to remove closures when producing the output messages
		foreach (var request in requests)
			await Producer.Send(request, OnResult);

		return;

		async Task OnResult(SendResult result) {
			results.Add(result);

			if (result.Failure) {
				// TODO SS: should we "raise" the exception twice? (one here and another in deactivation)
				await Intercept(new RecordProcessingError(this, record, requests, results, result.Error!));

				// don't wait, just let it flow...
				// this will trigger the cancellation of the token
				// and gracefully stop processing any other messages
				
				//if (State is ProcessorState.Running or ProcessorState.Suspended)
				_ = Deactivate(result.Error!);
			}
			else if (results.Count == requestCount) {
				await Consumer.Track(record);
				await Intercept(new RecordProcessed(this, record, results));
			}
		}
	}

	async Task Deactivate(Exception? exception) {
		if (State is ProcessorState.Stopped or ProcessorState.Deactivating) {
			await ChangeProcessorState(
				ProcessorState.Stopped, new InvalidOperationException(
					$"{ProcessorName} already {State.ToString().ToLower()}. This should not happen! Investigate!", exception
			));
			
			return; // already stopped...
		}

		await ChangeProcessorState(ProcessorState.Deactivating);

		try {
			// the consumer needs to first stop consuming...
			await Cancellator.CancelAsync();

			// flush the producer ensuring that all inflight output messages
			// are delivered, executing the callbacks and tracking positions
			await Producer.Flush();

			// disposing the consumer will also dispose of the checkpoint
			// controller and commit whatever positions are still pending
			await Consumer.DisposeAsync();
			
			// a mere formality, because the producer was already flushed
			await Producer.DisposeAsync();
		}
		catch (Exception vex) {
			vex = new($"{ProcessorName} stopped violently! {vex.Message}", vex);
			exception = exception is not null
				? new AggregateException(exception, vex).Flatten()
				: vex;
		}
		finally {
			await ChangeProcessorState(ProcessorState.Stopped, exception);

            await Interceptors.DisposeAsync();
            
			if (exception is not null)
				Finisher.TrySetException(exception);
			else
				Finisher.TrySetResult();
		}
	}
}