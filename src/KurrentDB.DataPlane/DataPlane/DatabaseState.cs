// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

internal abstract class DatabaseState : IAsyncDisposable {
	public readonly CancellationToken Token;
	private readonly Lock _syncRoot;
	private CancellationTokenSource? _cts;
	private Task _runningTask;

	private protected DatabaseState() {
		_syncRoot = new();
		_runningTask = Task.CompletedTask;
		_cts = new();
		Token = _cts.Token;
	}

	protected abstract Task RunAsync();

	public bool TryStart() {
		bool result;
		lock (_syncRoot) {
			result = _cts is not null;
			if (result) {
				_runningTask = RunAsync();
			}
		}

		return result;
	}

	private async Task StopAsync(Task runningTask, CancellationTokenSource cts) {
		using (cts) {
			await cts.CancelAsync();
		}

		await runningTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
		                                 | ConfigureAwaitOptions.SuppressThrowing);
	}

	public ValueTask DisposeAsync() {
		var task = ValueTask.CompletedTask;
		if (_cts is not null) {
			lock (_syncRoot) {
				if (_cts is { } cts) {
					_cts = null;
					task = new(StopAsync(_runningTask, cts));
				}
			}
		}

		return task;
	}
}
