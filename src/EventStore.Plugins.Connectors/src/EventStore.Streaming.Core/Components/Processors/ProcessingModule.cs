using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Processors;

[PublicAPI]
public abstract class ProcessingModule {
	MessageRouter Router { get; } = new();

	/// <summary>
	/// Registers a record handler for any message.
	/// </summary>
	protected virtual void Process(ProcessRecord handler) =>
		Router.RegisterHandler(new RecordHandler.Proxy(handler));
	
	/// <summary>
	/// Registers a record handler for a specific message.
	/// </summary>
	protected virtual void Process<T>(Route route, ProcessRecord<T> handler) => 
		Router.RegisterHandler(route, new RecordHandler<T>.Proxy(handler));
	
	/// <summary>
	/// Registers a record handler for a specific message.
	/// </summary>
	protected virtual void Process<T>(ProcessRecord<T> handler) => 
		Router.RegisterHandler(new RecordHandler<T>.Proxy(handler));
	
	/// <summary>
	/// Registers a record handler for any message.
	/// </summary>
	protected virtual void Process(ProcessRecordSynchronously handler) =>
		Router.RegisterHandler(new SynchronousRecordHandler.Proxy(handler));
	
	/// <summary>
	/// Registers a record handler for a specific message.
	/// </summary>
	protected virtual void Process<T>(Route route, ProcessRecordSynchronously<T> handler) => 
		Router.RegisterHandler(route, new SynchronousRecordHandler<T>.Proxy(handler));
	
	/// <summary>
	/// Registers a record handler for a specific message.
	/// </summary>
	protected virtual void Process<T>(ProcessRecordSynchronously<T> handler) =>
		Router.RegisterHandler(new SynchronousRecordHandler<T>.Proxy(handler));
	
	/// <summary>
	/// Attempts to process a record using the provided context.
	/// </summary>
	public Task ProcessRecord(RecordContext context) =>
		Router.ProcessRecord(context);
	
	/// <summary>
	/// Determines if the record can be processed by checking if there is any handler registered for the record.
	/// </summary>
	public bool CanProcessRecord(EventStoreRecord record) =>
		Router.CanProcessRecord(record);
	
	/// <summary>
	/// Combines the handlers from the current router with the provided router.
	/// </summary>
	public void RegisterHandlersOn(MessageRouter router) =>
		router.Combine(Router);

    /// <summary>
    /// Combines the handlers from the current router registry with the provided router registry.
    /// </summary>
    public void RegisterHandlersOn(MessageRouterRegistry registry) => 
        registry.Combine(Router.Registry);
}