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
public sealed class SecondaryIndexCheckpointTracker : IAsyncDisposable {
	private readonly int _batchSize;
	private readonly TimeSpan _timeout;
	private readonly Func<CancellationToken, ValueTask> _commitAction;
	private readonly AsyncManualResetEvent _signal = new(initialState: false);
	private readonly CancellationTokenSource _cts;
	private readonly Task _loopTask;

	private int _counter;
	private bool _disposed;

	public SecondaryIndexCheckpointTracker(
		int batchSize,
		uint delayMs,
		Func<CancellationToken, ValueTask> commitAction,
		CancellationToken ct) {

		_batchSize = batchSize;
		_timeout = TimeSpan.FromMilliseconds(delayMs);
		_commitAction = commitAction;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_loopTask = Loop(_cts.Token);
	}

	public async ValueTask DisposeAsync() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		using (_cts) {
			await _cts.CancelAsync();
		}
		await _loopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		_signal.Dispose();
	}

	public void Increment() {
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (Interlocked.Increment(ref _counter) >= _batchSize) {
			_signal.Set();
		}
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task Loop(CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			await _signal.WaitAsync(_timeout, ct);

			// reset the signal to avoid race conditions of two loops getting into this section
			_signal.Reset();

			// either signalled or timed out
			var count = Interlocked.Exchange(ref _counter, 0);

			if (count == 0)
				continue;

			try {
				await _commitAction(ct);
			} catch (OperationCanceledException) {
				// expected
			} catch (Exception ex) {
				Log.Error(ex, "Error during checkpoint commit");
			}
		}
	}
}
