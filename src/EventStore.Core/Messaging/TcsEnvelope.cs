// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;

namespace EventStore.Core.Messaging;

public class TcsEnvelope<TResult> : IEnvelope where TResult : class {
	private readonly TaskCompletionSource<TResult> _tcs;
	public Task<TResult> Task => _tcs.Task;

	public TcsEnvelope() {
		_tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	public void ReplyWith<T>(T message) where T : Message {
		if (message is not TResult result) {
			_tcs.TrySetException(new ArgumentException("Unexpected message type.", nameof(message)));
			return;
		}

		_tcs.TrySetResult(result);
	}
}
