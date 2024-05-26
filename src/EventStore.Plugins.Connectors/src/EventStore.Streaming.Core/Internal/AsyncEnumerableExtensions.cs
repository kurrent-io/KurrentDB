namespace EventStore.Streaming;

static class AsyncEnumerableExtensions {
	public static IAsyncEnumerable<T?> BreakOn<T, TException>(this IAsyncEnumerable<T?> self, Action<TException>? handler = null) where TException : Exception =>
		self.Catch<T?, TException>(
			ex => {
				handler?.Invoke(ex);
				return AsyncEnumerable.Empty<T?>();
			}
		);

	public static IAsyncEnumerable<T?> TakeUntilCancelled<T>(this IAsyncEnumerable<T?> self) =>
		self.BreakOn<T, OperationCanceledException>();
}