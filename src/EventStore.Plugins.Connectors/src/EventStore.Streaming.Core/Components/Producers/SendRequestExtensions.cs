namespace EventStore.Streaming.Producers;

[PublicAPI]
public static class SendRequestExtensions {
	/// <summary>
	///  Ensures that the stream is set on the request.
	/// </summary>
	/// <param name="request">
	/// The request to ensure the stream is set on.
	/// </param>
	/// <param name="defaultStream">
	/// The default stream to use if the stream is not set on the request.
	/// </param>
	/// <returns>
	/// A request with the stream set.
	/// <para/>
	/// If the stream is already set, the request will be returned as is.
	/// <para/>
	/// If the stream is not set, the default stream will be used instead.
	/// <para/>
	/// If the default stream is not set, an exception will be thrown.
	/// </returns>
	/// <exception cref="StreamRequiredError">
	/// Thrown when the stream is not set on the request and the default stream is not set.
	/// </exception>
	public static T EnsureStreamIsSet<T>(this T request, string? defaultStream) where T : struct, ISendRequest  =>
		request.HasStream switch {
			true                                 => request,
			false when defaultStream is not null => request with { Stream = defaultStream },
			_                                    => throw new StreamRequiredError(request.RequestId)
		};
}