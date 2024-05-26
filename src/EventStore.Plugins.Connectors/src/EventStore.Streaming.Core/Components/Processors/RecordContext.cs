using System.Collections.Concurrent;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Processors;

[PublicAPI]
public record RecordContext :  IMessageContext {
	public RecordContext(
		IProcessorMetadata processor,
		EventStoreRecord record,
		IStateStore state,
		ILogger logger,
		CancellationToken cancellationToken
	) {
        Record            = record;
        State             = state;
        Logger            = logger;
        CancellationToken = cancellationToken;
        Processor         = processor;
	}
    

    ConcurrentQueue<SendRequest> MessageQueue { get; } = new();
	InterlockedBoolean           QueueLocked  { get; } = new();

	public IProcessorMetadata       Processor         { get; }
	public EventStoreRecord         Record            { get; }
	public IStateStore              State             { get; }
	public ILogger                  Logger            { get; }
	public CancellationToken        CancellationToken { get; }

    MessageContextProperties IMessageContext.Properties { get; }  = new();

    /// <summary>
	/// Enqueues message to be sent on exit
	/// </summary>
	public void Output(SendRequest request) {
		if (QueueLocked.CurrentValue)
			throw new InvalidOperationException("Messages already processed. Ensure any async operations are awaited.");

		MessageQueue.Enqueue(request);
	}

    /// <summary>
    /// Enqueues message to be sent on exit
    /// </summary>
    public void Output<T>(T message) where T : class =>
        Output(SendRequest.Builder.Message(message).Create());
    
	/// <summary>
	/// Enqueues message to be sent on exit
	/// </summary>
	public void Output<T>(T message, StreamId streamId) where T : class =>
		Output(SendRequest.Builder.Message(message).Stream(streamId).Create());
	
	/// <summary>
	/// Enqueues message to be sent on exit
	/// </summary>
	public void Output<T>(T message, PartitionKey key) where T : class =>
		Output(SendRequest.Builder.Message(message, key).Create());
	
	/// <summary>
	/// Enqueues message to be sent on exit
	/// </summary>
	public void Output<T>(T message, PartitionKey key, StreamId streamId) where T : class =>
		Output(SendRequest.Builder.Message(message, key).Stream(streamId).Create());
    
	public SendRequest[] Requests(bool clear = false) {
		QueueLocked.EnsureCalledOnce();

		var messages = MessageQueue.ToArray();
        
		if (clear) MessageQueue.Clear();
        
		return messages;
	}

	public void ClearOutput() => MessageQueue.Clear();

	public dynamic Message => Record.Value;
}