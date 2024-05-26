using EventStore.Streaming.Consumers;
using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Processors;

public interface IProcessorMetadata {
    public string                            ProcessorId      { get; }
    public string                            ProcessorName    { get; }
	public string                            SubscriptionName { get; }
	public string[]                          Streams          { get; }
	public ConsumeFilter                     Filter           { get; }
	public ProcessorState                    State            { get; }
	public IReadOnlyCollection<EndpointInfo> Endpoints        { get; }
	public Type                              ProcessorType    { get; }
}

public interface IProcessor : IProcessorMetadata, IAsyncDisposable {
	/// <summary>
	/// A task that completes when the processor has stopped.
	/// </summary>
	Task Stopped { get; }

	/// <summary>
	/// Attempts to start and activate the processor.
	/// </summary>
	Task Activate(CancellationToken stoppingToken);

	/// <summary>
	/// Pauses the processor.
	/// </summary>
	Task Suspend();

	/// <summary>
	/// Resumes the processor, if it is in a suspended state.
	/// </summary>
	Task Resume();

	/// <summary>
	/// Attempts to stop and deactivate the processor.
	/// </summary>
	Task Deactivate();
}

// The operational state of a connector.
public enum ProcessorState {
	/// <summary>
	/// Do not use this default value.
	/// </summary>
	Unspecified = 0,

	/// <summary>
	/// The processor is initializing and getting ready to run.
	/// </summary>
	Activating = 1,

	/// <summary>
	/// The processor has been successfully initialized and is currently running.
	/// </summary>
	Running = 2,

	/// <summary>
	/// The processor is paused. It is not consuming records, but it is still running.
	/// </summary>
	Suspended = 3,

	/// <summary>
	/// The processor is in the process of stopping.
	/// </summary>
	Deactivating = 4,

	/// <summary>
	/// The processor has been stopped, either due to a manual command or an error.
	/// </summary>
	Stopped = 5,

	// /// <summary>
	// /// The processor has encountered an error that prevents it from functioning correctly.
	// /// Perhaps it stopped unexpectedly or failed to activate.
	// /// Failed means the processor is also stopped and not running.
	// /// </summary>
	// Failed = 6
}