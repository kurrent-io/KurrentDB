namespace EventStore.Streaming.Producers;

public interface IProducerMetadata {
	/// <summary>
	/// The name of the producer.
	/// </summary>
	public string ProducerName { get; }

	/// <summary>
	/// The default stream the producer will target.
	/// </summary>
	public string? Stream { get; }

	/// <summary>
	/// The number of messages that are currently in flight.
	/// </summary>
	public int InFlightMessages { get; }
}

public interface IProducer : IProducerMetadata, IAsyncDisposable {
	/// <summary>
	/// Sends a message without waiting for write confirmation.
	/// </summary>
	/// <param name="request">
	/// The request to send containing the message.
	/// </param>
	/// <param name="onResult">
	/// The callback to invoke when the message is written to the database.
	/// </param>
	Task Send(SendRequest request, OnSendResult onResult);

	/// <summary>
	/// Sends a message without waiting for write confirmation.
	/// </summary>
	/// <param name="request">
	/// The request to send containing the message.
	/// </param>
	/// <param name="onResult">
	/// The callback to invoke when the message is written to the database.
	/// </param>
	/// <param name="state">
	/// The state to pass to the callback.
	/// </param>
	Task Send<TState>(SendRequest request, OnSendResult<TState> onResult, TState state);

	/// <summary>
	/// Sends a message without waiting for write confirmation.
	/// </summary>
	/// <param name="request">
	/// The request to send containing the message.	
	/// </param>
	/// <param name="callback">
	/// The callback to invoke when the message is written to the database.
	/// </param>
	/// <typeparam name="TState">
	/// The state to pass to the callback.
	/// </typeparam>
	Task Send<TState>(SendRequest request, SendResultCallback<TState> callback);

	/// <summary>
	/// Sends a message without waiting for write confirmation.
	/// </summary>
	/// <param name="request">
	/// The request to send containing the message.	
	/// </param>
	/// <param name="callback">
	/// The callback to invoke when the message is written to the database.
	/// </param>
	Task Send(SendRequest request, SendResultCallback callback);

	/// <summary>
	/// Waits for all inflight messages to be sent.
	/// </summary>
	/// <param name="cancellationToken">
	/// The optional cancellation token used to interrupt the flushing process.
	/// </param>
	/// <returns>
	/// The number of handled and pending messages,
	/// in case the cancellation token was triggered
	/// and the operation was not completed.
	/// </returns>
	Task<(int Flushed, int Inflight)> Flush(CancellationToken cancellationToken = default);
}

public delegate Task OnSendResult(SendResult result);

public delegate Task OnSendResult<in T>(SendResult result, T state);

public class Callback<TResult, TState>(Func<TResult, TState, Task> onCallback, TState state) {
	Func<TResult, TState, Task> OnCallback { get; } = onCallback;
	TState                      State      { get; } = state;

	public Task Execute(TResult result) => OnCallback(result, State);
}

public interface ISendResultCallback {
	Task Execute(SendResult result);
}

public class SendResultCallback<TState>(OnSendResult<TState> onResult, TState onResultState)
	: Callback<SendResult, (OnSendResult<TState> OnUserSendResult, TState UserState)>(
		static (result, state) => state.OnUserSendResult(result, state.UserState),
		(onResult, onResultState)
	), ISendResultCallback;
	
public class SendResultCallback(OnSendResult onResult)
	: Callback<SendResult, OnSendResult>(
		static (result, onResult) => onResult(result), 
		onResult
	), ISendResultCallback;
