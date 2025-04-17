// using Microsoft.Extensions.ObjectPool;
//
// namespace EventStore.Toolkit.Testing;
//
// [PublicAPI]
// public static class TimeoutExtensions {
// 	static readonly ObjectPool<CancellationTokenSource> TimeoutTokenSourcePool = ObjectPool.Create<CancellationTokenSource>();
//
// 	public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout, string? message, Func<Task<TResult>> onTimeout) {
// 		var cancellator = TimeoutTokenSourcePool.Get().With(x => x.CancelAfter(timeout));
//
// 		try {
// 			if (await Task.WhenAny(task, Task.Delay(timeout, cancellator.Token)).ConfigureAwait(false) != task)
// 				return await onTimeout().ConfigureAwait(false);
//
// 			await cancellator.CancelAsync().ConfigureAwait(false);
// 			return await task.ConfigureAwait(false);
// 		}
// 		finally {
// 			if (cancellator.TryReset())
// 				TimeoutTokenSourcePool.Return(cancellator);
// 			else
// 				cancellator.Dispose();
// 		}
// 	}
//
// 	public static Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout, string? message = null) =>
// 		task.TimeoutAfter(timeout, message, () => throw new TimeoutException(message ?? $"Timed out after waiting {timeout.TotalMilliseconds}ms"));
//
// 	// public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout, string? message = null) {
// 	// 	var cancellator = TimeoutTokenSourcePool.Get().With(x => x.CancelAfter(timeout));
// 	//
// 	// 	try {
// 	// 		if (await Task.WhenAny(task, Task.Delay(timeout, cancellator.Token)).ConfigureAwait(false) != task)
// 	// 			throw new TimeoutException(message ?? $"Timed out after waiting {timeout.TotalMilliseconds}ms");
// 	//
// 	// 		cancellator.Cancel();
// 	// 		return await task.ConfigureAwait(false);
// 	// 	}
// 	// 	finally {
// 	// 		// Not raised timeout, you can reuse by Reset
// 	// 		if (cancellator.TryReset())
// 	// 			TimeoutTokenSourcePool.Return(cancellator);
// 	// 	}
// 	// }
//
// 	// public static Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int timeoutMs, string? message = null) =>
// 	// 	task.TimeoutAfter(TimeSpan.FromMilliseconds(timeoutMs), message);
//
// 	public static Task TimeoutAfter(this Task task, TimeSpan timeout, string? message = null) =>
// 		TimeoutAfter(task.ContinueWith(t => t.IsCompletedSuccessfully ? true : throw t.Exception!.InnerException ?? t.Exception), timeout, message);
//
// 	// public static Task TimeoutAfter(this Task task, int timeoutMs, string? message = null) =>
// 	// 	task.TimeoutAfter(TimeSpan.FromMilliseconds(timeoutMs), message);
//
// 	public static async ValueTask<TResult> TimeoutAfter<TResult>(this ValueTask<TResult> valueTask, TimeSpan timeout, string? message = null) =>
// 		await valueTask.AsTask().TimeoutAfter(timeout, message).ConfigureAwait(false);
//
// 	// public static ValueTask<TResult> TimeoutAfter<TResult>(this ValueTask<TResult> valueTask, int timeoutMs, string? message = null) =>
// 	// 	valueTask.TimeoutAfter(TimeSpan.FromMilliseconds(timeoutMs), message);
// }