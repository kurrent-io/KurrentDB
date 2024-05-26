namespace EventStore.Streaming;

[PublicAPI]
public abstract class StreamingError(string message, bool isTransient = false, Exception? inner = null) : Exception(message, inner) {
	public bool IsCritical  { get; } = !isTransient;
	public bool IsTransient { get; } = isTransient;
}

[PublicAPI]
public class StreamingTransientError(string message, Exception? inner = null) : StreamingError( message, true, inner);

[PublicAPI]
public class StreamingCriticalError(string message, Exception? inner = null) : StreamingError(message, false, inner);

public class StreamRequiredError(Guid requestId) : StreamingCriticalError($"Request '{requestId}' is missing destination stream");

public class StreamDeletedError(string stream) : StreamingCriticalError($"Stream '{stream}' deleted") {
	/// <summary>The name of the deleted stream.</summary>
	public readonly string Stream = stream;
}

public class StreamNotFoundError(string stream) : StreamingCriticalError($"Stream '{stream}' not found") {
	/// <summary>The name of stream.</summary>
	public readonly string Stream = stream;
}

public class ExpectedStreamRevisionError(string stream, StreamRevision expectedRevision, StreamRevision actualRevision)
	: StreamingCriticalError($"Stream '{stream}' revision was {actualRevision} but request expected {expectedRevision}") {

	public ExpectedStreamRevisionError(string stream, long expectedRevision, long actualRevision) 
		: this(stream, StreamRevision.From(expectedRevision), StreamRevision.From(actualRevision)) { }
	
	/// <summary>The name of the stream.</summary>
	public readonly string Stream = stream;
	
	/// <summary>
	/// If available, the expected <see cref="T:EventStore.Streaming.StreamRevision" /> specified for the operation that failed.
	/// </summary>
	public readonly StreamRevision ExpectedStreamRevision = expectedRevision;

	/// <summary>
	/// The current <see cref="T:EventStore.Streaming.StreamRevision" /> of the stream that the operation was attempted on.
	/// </summary>
	public readonly StreamRevision ActualStreamRevision = actualRevision;
}

public class RequestTimeoutError(string stream, string? reason = null) : StreamingTransientError($"Stream '{stream}' request timed out: {reason}") {
	/// <summary>The name of the stream.</summary>
	public readonly string Stream = stream;
}

public class StreamAccessDeniedError(string stream) : StreamingCriticalError($"Stream '{stream}' access denied") {
	/// <summary>The name of the stream.</summary>
	public readonly string Stream = stream;
}

public class AccessDeniedError() : StreamingCriticalError("Access denied");

public class ServerNotReadyError() : StreamingTransientError("Server not ready");

public class ServerTooBusyError() : StreamingTransientError("Server too busy");

public class ServerNotLeaderError : StreamingTransientError {
	public ServerNotLeaderError(string host, int port) : base($"Server is not the leader in the topology - {host}:{port}") { }
	
	public ServerNotLeaderError() : base("Server is not the leader in the topology") { }
}