namespace EventStore.Streaming.Interceptors;

/// <summary>
/// The severity of reported component event.
/// </summary>
public enum InterceptorEventSeverity {
	/// <summary>
	/// The event is not recorded.
	/// </summary>
	None = 0,

	/// <summary>
	/// The event is used for debugging purposes only.
	/// </summary>
	Debug,

	/// <summary>
	/// The event is informational.
	/// </summary>
	Information,

	/// <summary>
	/// The event should be treated as a warning.
	/// </summary>
	Warning,

	/// <summary>
	/// The event should be treated as an error.
	/// </summary>
	Error,

	/// <summary>
	/// The event should be treated as a critical error.
	/// </summary>
	Critical
}