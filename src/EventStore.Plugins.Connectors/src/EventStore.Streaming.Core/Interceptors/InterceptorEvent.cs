namespace EventStore.Streaming.Interceptors;

public abstract record InterceptorEvent {
	/// <summary>
	/// Initializes a new instance of the <see cref="InterceptorEvent"/> record.
	/// </summary>
	/// <param name="severity">The severity of the event.</param>
	/// <param name="timestamp">The timestamp of the event. If <see langword="null"/>, the current time is used.</param>
	/// <param name="eventName">The event name.</param>
	protected InterceptorEvent(
		InterceptorEventSeverity severity = InterceptorEventSeverity.Information,
		DateTimeOffset? timestamp = null, 
		string? eventName = null
	) {
		Severity  = severity;
		EventName = eventName ?? GetType().Name;
		Timestamp = timestamp ?? DateTimeOffset.UtcNow;
		Id        = Guid.NewGuid();
	}

	public DateTimeOffset Timestamp { get; }

	/// <summary>
	/// Gets the severity of the event.
	/// </summary>
	public InterceptorEventSeverity Severity { get; }

	/// <summary>
	/// Gets the event name.
	/// </summary>
	public string EventName { get; }

	/// <summary>
	/// Gets the unique identifier of the event.
	/// </summary>
	public Guid Id { get; } 
	
	/// <summary>
	/// Returns an <see cref="EventName"/>.
	/// </summary>
	/// <returns>An event name.</returns>
	public override string ToString() => EventName;
}