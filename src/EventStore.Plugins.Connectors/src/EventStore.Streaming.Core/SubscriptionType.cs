namespace EventStore.Streaming;

public enum SubscriptionType {
	/// <summary>
	/// There can be only 1 consumer on the same topic with the same subscription name.
	/// </summary>
	Exclusive = 0,

	/// <summary>
	/// Multiple consumers will be able to use the same subscription name and the messages will be dispatched according to a round-robin rotation.
	/// </summary>
	Shared = 1,

	/// <summary>
	/// Multiple consumers will be able to use the same subscription name but only 1 consumer will receive the messages.
	/// </summary>
	Failover = 2,

	/// <summary>
	/// Multiple consumers will be able to use the same subscription name and the messages will be dispatched according to the key.
	/// </summary>
	KeyShared = 3
}