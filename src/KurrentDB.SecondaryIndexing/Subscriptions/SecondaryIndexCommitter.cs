// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using DotNext.Threading;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

// Calls commitAction when the delay is reached or when Increment is called enough times
// to reach the batch size. Either trigger resets the increment count and the timeout.
// commitAction is only ever called if Increment has been called at least once.
public sealed class SecondaryIndexCommitter : IAsyncDisposable {
	private readonly int _batchSize;
	private readonly TimeSpan _timeout;
	private readonly Action _commitAction;
	private readonly AsyncAutoResetEvent _signal = new(initialState: false);
	private volatile CancellationTokenSource? _cts; // null if disposed
	private readonly Task _loopTask;

	private volatile int _counter;

	public SecondaryIndexCommitter(
		int batchSize,
		uint delayMs,
		Action commitAction,
		CancellationToken ct) {

		_batchSize = batchSize;
		_timeout = TimeSpan.FromMilliseconds(delayMs);
		_commitAction = commitAction;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_loopTask = Loop(_cts.Token);
	}

	public ValueTask DisposeAsync() {
		// dispose CTS once to deal with the concurrent call to the current method
		if (Interlocked.Exchange(ref _cts, null) is not { } cts)
			return ValueTask.CompletedTask;

		using (cts) {
			cts.Cancel();
		}

		return DisposeCoreAsync();
	}

	private async ValueTask DisposeCoreAsync() {
		// use ContinueOnCapturedContext for consistency with the rest of the code
		// in the project, since we don't use explicit ConfigureAwait call
		await _loopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
		                               ConfigureAwaitOptions.ContinueOnCapturedContext);

		_signal.Dispose();
	}

	private bool IsDisposed => _cts is null;

	public void Increment() {
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		// Trigger the loop once when overflow detected, this allows avoiding
		// multiple calls to Set. The loop sets the counter to zero
		// in case of any overflow (even if it's more than 1 multiple of the batch size).
		if (Interlocked.Increment(ref _counter) == _batchSize) {
			_signal.Set();
		}
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task Loop(CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			await _signal.WaitAsync(_timeout, ct);

			// either signalled or timed out
			var count = Interlocked.Exchange(ref _counter, 0);

			if (count is 0)
				continue;

			try {
				_commitAction();
			} catch (Exception ex) {
				Log.Error(ex, "Error during checkpoint commit");
			}
		}
	}
}
