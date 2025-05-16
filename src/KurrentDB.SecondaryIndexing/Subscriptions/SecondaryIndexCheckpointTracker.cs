// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog;

namespace KurrentDB.SecondaryIndexing.Subscriptions;

public sealed class SecondaryIndexCheckpointTracker(
	int batchSize,
	uint delayMs,
	Func<CancellationToken, Task> commitAction
) : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexCheckpointTracker>();

	private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(delayMs);
	private readonly object _startLock = new();

	private readonly SemaphoreSlim _incrementSignal = new(0, 1);
	private int _counter;

	private int _isCommitting;

	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;

	private bool _disposed;

	public void Start(CancellationToken externalToken) {
		ThrowIfDisposed();

		lock (_startLock) {
			if (_loopTask != null)
				throw new InvalidOperationException("Already started");

			_loopCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
			_loopTask = RunAsync(_loopCts.Token);
		}
	}

	public void Increment() {
		ThrowIfDisposed();

		int count = Interlocked.Increment(ref _counter);
		if (count < batchSize) return;

		if (_incrementSignal.CurrentCount == 0) {
			_incrementSignal.Release();
		}
	}

	private async Task RunAsync(CancellationToken token) {
		using var timer = new PeriodicTimer(_interval);

		try {
			while (!token.IsCancellationRequested) {
				var signalTask = _incrementSignal.WaitAsync(token);
				var timerTask = timer.WaitForNextTickAsync(token).AsTask();

				await Task.WhenAny(signalTask, timerTask).ConfigureAwait(false);

				if (token.IsCancellationRequested)
					break;

				await TryCommitAsync(token).ConfigureAwait(false);
			}
		} catch (OperationCanceledException) {
			// Expected during cancellation
		} catch (Exception ex) {
			Log.Error(ex, "Unexpected error in checkpoint loop");
		}
	}

	private async Task TryCommitAsync(CancellationToken token) {
		if (Interlocked.CompareExchange(ref _isCommitting, 1, 0) != 0)
			return;

		try {
			int count = Interlocked.Exchange(ref _counter, 0);
			if (count == 0)
				return;

			await commitAction(token).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Expected during cancellation
		} catch (Exception ex) {
			Log.Error(ex, "Error during checkpoint commit");
		} finally {
			Interlocked.Exchange(ref _isCommitting, 0);
		}
	}

	public async ValueTask DisposeAsync() {
		if (_disposed) return;

		CancellationTokenSource? localCts = null;
		Task? localTask = null;

		lock (_startLock) {
			if (_disposed) return;

			_disposed = true;

			if (_loopCts != null) {
				localCts = _loopCts;
				localTask = _loopTask;
				_loopCts = null;
				_loopTask = null;
			}
		}

		if (localCts != null) {
			try {
				await localCts.CancelAsync().ConfigureAwait(false);

				if (localTask != null)
					await localTask.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected during cancellation
			} catch (Exception ex) {
				Log.Error(ex, "Error during checkpoint tracker disposal");
			} finally {
				localCts.Dispose();
			}
		}

		_incrementSignal.Dispose();
	}

	private void ThrowIfDisposed() {
		if (!_disposed) return;
		throw new ObjectDisposedException(nameof(SecondaryIndexCheckpointTracker));
	}
}
