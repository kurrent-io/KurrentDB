// ReSharper disable CheckNamespace

using EventStore.Streaming.Interceptors;

namespace EventStore.Streaming.Producers.LifecycleEvents;

public abstract record ProducerLifecycleEvent(IProducerMetadata Producer) : InterceptorEvent;

public record SendRequestReceived(IProducerMetadata Producer, SendRequest Request) : ProducerLifecycleEvent(Producer);

/// <summary>
/// Request received, serialized and waiting to be sent.  
/// </summary>
public record SendRequestReady(IProducerMetadata Producer, SendRequest Request) : ProducerLifecycleEvent(Producer);

public record SendRequestProcessed(IProducerMetadata Producer, SendResult Result) : ProducerLifecycleEvent(Producer);

public record SendRequestSucceeded(IProducerMetadata Producer, SendRequest Request, RecordPosition Position) : ProducerLifecycleEvent(Producer);

public record SendRequestFailed(IProducerMetadata Producer, SendRequest Request, StreamingError Error) : ProducerLifecycleEvent(Producer);

public record SendRequestCallbackError(IProducerMetadata Producer, SendResult Result, Exception Error) : ProducerLifecycleEvent(Producer);

public record ProducerFlushed(IProducerMetadata Producer, int Handled, int Inflight) : ProducerLifecycleEvent(Producer);

public record ProducerStopped(IProducerMetadata Producer, Exception? Error = null) : ProducerLifecycleEvent(Producer);
