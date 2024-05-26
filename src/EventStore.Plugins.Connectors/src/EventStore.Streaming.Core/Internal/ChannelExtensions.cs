using System.Threading.Channels;

namespace EventStore.Streaming;

[PublicAPI]
static class ChannelExtensions {
	public static IAsyncEnumerable<T?> ReadAllUntilCancelled<T>(this ChannelReader<T?> reader, CancellationToken cancellationToken) =>
		reader.ReadAllAsync(cancellationToken).TakeUntilCancelled();

	
	public static IAsyncEnumerable<T?> ReadAllUntilCancelled<T>(this Channel<T?> channel, CancellationToken cancellationToken) =>
		channel.Reader.ReadAllUntilCancelled(cancellationToken);
	
	public static async Task WaitUntilEmpty<T>(this Channel<T> channel, TimeSpan checkDelay, CancellationToken cancellationToken = default) {
		while (!cancellationToken.IsCancellationRequested && channel.Reader.TryPeek(out _))
			await Tasks.SafeDelay(checkDelay, cancellationToken).ConfigureAwait(false);
	}
	
	public static Task WaitUntilEmpty<T>(this Channel<T> channel, double checkDelayMs = 25, CancellationToken cancellationToken = default) => 
		WaitUntilEmpty(channel, TimeSpan.FromMilliseconds(checkDelayMs), cancellationToken);
	
	public static Task WaitUntilEmpty<T>(this Channel<T> channel, CancellationToken cancellationToken = default) => 
		WaitUntilEmpty(channel, 25, cancellationToken);

	public static async Task Complete<T>(this Channel<T> channel, Exception? error = null) {
		try {
			channel.Writer.Complete(error);
			await channel.Reader.Completion.ConfigureAwait(false);
		}
		catch (InvalidOperationException) {
			// ignore
		}
		catch (OperationCanceledException) {
			// ignore
		}
	}
	
	public static async Task HandleAll<T>(this ChannelReader<T> reader, Func<T, Task> handler, CancellationToken cancellationToken = default) {
		try {
			do {
				while (!cancellationToken.IsCancellationRequested && reader.TryPeek(out var item)) {
					await handler(item).ConfigureAwait(false);
					reader.TryRead(out _); // ack
				}
			} while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (OperationCanceledException) {
			// in case WaitToReadAsync is cancelled.
		}
	}
}