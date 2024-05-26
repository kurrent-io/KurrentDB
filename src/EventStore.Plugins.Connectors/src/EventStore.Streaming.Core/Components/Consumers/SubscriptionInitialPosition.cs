namespace EventStore.Streaming.Consumers;

/// <summary>
/// Initial position at which the cursor will be set when subscribing if no checkpoint is found.
/// </summary>
public enum SubscriptionInitialPosition {
	/// <summary>
	/// Consumption will start at the last message.
	/// </summary>
	Latest = 0,

	/// <summary>
	/// Consumption will start at the first message.
	/// </summary>
	Earliest = 1
}