using static System.Threading.Tasks.TaskCreationOptions;

namespace EventStore.Streaming.Producers;

[PublicAPI]
public static class ProducerExtensions {
	public static Task Send(this IProducer producer, SendRequest request, Action<SendResult> onResult) =>
		producer.Send(
			request, static (result, userOnResult) => {
				userOnResult(result);
				return Task.CompletedTask;
			}, onResult
		);

	// public static Task Send<T>(this IProducer producer, SendRequest request, Action<SendResult, T> onResult, T onResultState) {
	// 	
	// 	var callback = new SendResultCallback<T>(
	// 		(result, state) => {
	// 			onResult(result, state);
	// 			return Task.CompletedTask;
	// 		},
	// 		onResultState
	// 	);
	// 	
	// 	return producer.Send(
	// 		request,
	// 		new SendResultCallback<T>(
	// 			(result, state) => {
	// 				onResult(result, state);
	// 				return Task.CompletedTask;
	// 			},
	// 			onResultState
	// 		)
	// 	);
	// }
	
	public static async Task<SendResult> Send(this IProducer producer,SendRequest request, bool throwOnError = true) {
		var operation = new TaskCompletionSource<SendResult>(RunContinuationsAsynchronously);

		try {
			await producer.Send(
				request, static (result, state) => {
					if (result.Success) state.operation.TrySetResult(result);
					else if (state.throwOnError) state.operation.TrySetException(result.Error!);
					else state.operation.TrySetResult(result);

					return Task.CompletedTask;
				}, (operation, throwOnError)
			).ConfigureAwait(true);
		}
		catch (Exception ex) {
			operation.TrySetException(ex);
		}

		return await operation.Task;
	}

	public static Task<SendResult> Send<T>(this IProducer producer, T message, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(message)
			.Create();
        
		return producer.Send(req, throwOnException);
	}
	
	public static Task<SendResult> Send<T>(this IProducer producer, T message, Headers headers, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(msg => msg.Value(message).Headers(headers))
			.Create();
        
		return producer.Send(req, throwOnException);
	}
	
	public static Task<SendResult> Send<T>(this IProducer producer, T message, PartitionKey key, Headers headers, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(msg => msg.Value(message).Key(key).Headers(headers))
			.Create();
        
		return producer.Send(req, throwOnException);
	}
	
	public static Task<SendResult> Send(this IProducer producer, Message message, bool throwOnException = true) {
		var req = SendRequest.Builder
			.Message(message)
			.Create();
        
		return producer.Send(req, throwOnException);
	}

	public static Task<SendResult> Send<T>(this IProducer producer, T message, string stream, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(message)
			.Stream(stream)
			.Create();

		return producer.Send(req, throwOnException);
	}

	public static Task<SendResult> Send<T>(this IProducer producer, T message, PartitionKey key, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(x => x.Value(message).Key(key))
			.Create();

		return producer.Send(req, throwOnException);
	}

	public static Task<SendResult> Send<T>(this IProducer producer, T message, PartitionKey key, string stream, bool throwOnException = true) where T : class {
		var req = SendRequest.Builder
			.Message(x => x.Value(message).Key(key))
			.Stream(stream)
			.Create();

		return producer.Send(req, throwOnException);
	}

	public static Task Send<T>(this IProducer producer, T message, PartitionKey key, Action<SendResult> onResult) where T : class {
		var req = SendRequest.Builder
			.Message(x => x.Value(message).Key(key))
			.Create();

		return producer.Send(req, onResult);
	}

	public static Task Send<T>(this IProducer producer, T message, PartitionKey key, string stream, Action<SendResult> onResult) where T : class {
		var req = SendRequest.Builder
			.Message(x => x.Value(message).Key(key))
			.Stream(stream)
			.Create();

		return producer.Send(req, onResult);
	}

	public static Task Send<T>(this IProducer producer, T message, Action<SendResult> onResult) where T : class {
		var req = SendRequest.Builder
			.Message(message)
			.Create();

		return producer.Send(req, onResult);
	}

	public static Task Send<T>(this IProducer producer, T message, string stream, Action<SendResult> onResult) where T : class {
		var req = SendRequest.Builder
			.Message(message)
			.Stream(stream)
			.Create();

		return producer.Send(req, onResult);
	}
}