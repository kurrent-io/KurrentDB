using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Processors.Interceptors;

public abstract record ProcessorLifecycleEvent(
	IProcessorMetadata Processor, 
	Exception? Error = null, 
	InterceptorEventSeverity Severity = InterceptorEventSeverity.Information
) : InterceptorEvent(Severity) {
	//public override string ToString() => $"{Processor.ConsumerName} {GetType().Name.Replace("Processor", "")} ";
}

#region . state .

public record ProcessorStateChanged(IProcessorMetadata Processor, ProcessorState FromState, Exception? Error = null)
	: ProcessorLifecycleEvent(Processor, Error, Error is not null ? InterceptorEventSeverity.Critical : InterceptorEventSeverity.Information) {

	public ProcessorState ToState { get; } = Processor.State;
}

public record ProcessorActivating(IProcessorMetadata Processor) 
	: ProcessorLifecycleEvent(Processor);

public record ProcessorRunning(IProcessorMetadata Processor) 
	: ProcessorLifecycleEvent(Processor);

public record ProcessorSuspended(IProcessorMetadata Processor) 
	: ProcessorLifecycleEvent(Processor);

public record ProcessorDeactivating(IProcessorMetadata Processor) 
	: ProcessorLifecycleEvent(Processor);

public record ProcessorStopped(IProcessorMetadata Processor, Exception? Error = null) 
	: ProcessorLifecycleEvent(Processor, Error);


#endregion . state .

#region . records .

/// <summary>
/// Record processing skipped because there are no user handlers registered.
/// </summary>
public record RecordSkipped(IProcessorMetadata Processor, EventStoreRecord Record) 
	: ProcessorLifecycleEvent(Processor);

/// <summary>
/// Record received, deserialized and waiting to be processed.  
/// </summary>
public record RecordReady(IProcessorMetadata Processor, EventStoreRecord Record) 
	: ProcessorLifecycleEvent(Processor);

/// <summary>
/// Record handled by user processing logic.
/// </summary>
public record RecordHandled(IProcessorMetadata Processor, EventStoreRecord Record, IReadOnlyCollection<SendRequest> Requests) 
	: ProcessorLifecycleEvent(Processor);

/// <summary>
/// Record handling failed.
/// </summary>
public record RecordHandlingError(IProcessorMetadata Processor, EventStoreRecord Record, Exception Error) 
	: ProcessorLifecycleEvent(Processor, Error, InterceptorEventSeverity.Critical);

/// <summary>
/// Any output messages were sent and acknowledged by the server and the record was tracked.
/// </summary>
public record RecordProcessed(IProcessorMetadata Processor, EventStoreRecord Record, IReadOnlyCollection<SendResult> Results)
	: ProcessorLifecycleEvent(Processor) {
	public RecordProcessed(IProcessorMetadata Processor, EventStoreRecord Record) : this(Processor, Record, []) { }
}

/// <summary>
/// Record processing failed.
/// </summary>
public record RecordProcessingError(
	IProcessorMetadata Processor,
	EventStoreRecord Record,
	IReadOnlyCollection<SendRequest> Requests,
	IReadOnlyCollection<SendResult> Results,
	Exception Error
) : ProcessorLifecycleEvent(Processor, Error, InterceptorEventSeverity.Critical);

#endregion . records .