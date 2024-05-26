namespace EventStore.Streaming.Producers;

[PublicAPI]
public readonly record struct SendResult() {
	public SendRequest     Request  { get; private init; } = SendRequest.Empty;
	public RecordPosition  Position { get; private init; } = RecordPosition.Unset;
	public StreamingError? Error    { get; private init; }

	public Guid     RequestId => Request.RequestId;
	public StreamId Stream    => Request.Stream;

	public bool Success => Error is null;
	public bool Failure => Error is not null;

	public IReadOnlyList<Message> Messages => Request.Messages;

	public static SendResult Succeeded(SendRequest request, RecordPosition position) =>
		new() {
			Request  = request,
			Position = position
		};

	public static SendResult Failed(SendRequest request, StreamingError error) =>
		new() {
			Request = request,
			Error   = error
		};

	public override string ToString() =>
		Success
			? $"Success {nameof(RequestId)}: {RequestId}, {nameof(Position)}: {Position}"
			: $"Failure {nameof(RequestId)}: {RequestId}, {nameof(Error)}: {Error!.GetType().Name} {Error!.Message}";
}

readonly record struct SendResult<T>() where T : ISendRequest, new() {
    public T               Request  { get; private init; } = new();
    public RecordPosition  Position { get; private init; } = RecordPosition.Unset;
    public StreamingError? Error    { get; private init; }

    public Guid     RequestId => Request.RequestId;
    public StreamId Stream    => Request.Stream;

    public bool Success => Error is null;
    public bool Failure => Error is not null;

    public static SendResult<T> Succeeded(T request, RecordPosition position) =>
        new() {
            Request  = request,
            Position = position
        };

    public static SendResult<T> Failed(T request, StreamingError error) =>
        new() {
            Request = request,
            Error   = error
        };

    public override string ToString() =>
        Success
            ? $"Success {nameof(RequestId)}: {RequestId}, {nameof(Position)}: {Position}"
            : $"Failure {nameof(RequestId)}: {RequestId}, {nameof(Error)}: {Error!.GetType().Name} {Error!.Message}";
}
