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
public sealed class Committer {
	private readonly int _batchSize;
	private readonly Action _commitAction;
	private volatile int _counter;

	public Committer(int batchSize, Action commitAction, CancellationToken ct) {
		_batchSize = batchSize;
		_commitAction = commitAction;
	}

	public void Increment() {
		// Trigger the loop once when overflow detected, this allows avoiding
		// multiple calls to Set. The loop sets the counter to zero
		// in case of any overflow (even if it's more than 1 multiple of the batch size).
		if (Interlocked.Increment(ref _counter) == _batchSize) {
			_commitAction();
			Interlocked.Exchange(ref _counter, 0);
		}
	}
}
