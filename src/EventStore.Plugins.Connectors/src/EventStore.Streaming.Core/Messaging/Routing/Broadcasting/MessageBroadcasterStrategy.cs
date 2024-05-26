namespace EventStore.Streaming.Routing.Broadcasting;

public enum MessageBroadcasterStrategy {
	/// Executes all handlers one by one, according to their registration order
	Sequential,

	/// Executes all handlers in parallel
	Parallel,

	/// Executes all handlers at the same time and awaits for them to complete
	Async
}