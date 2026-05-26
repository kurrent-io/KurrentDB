// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext;

/// <summary>
/// Shared signal that completes when all Kontext services are initialized.
/// Services can await <see cref="WaitAsync"/> before processing requests.
/// </summary>
public class KontextReadySignal {
	readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task WaitAsync() => _tcs.Task;

	public void SetReady() => _tcs.TrySetResult();

	public void SetFailed(Exception ex) => _tcs.TrySetException(ex);
}