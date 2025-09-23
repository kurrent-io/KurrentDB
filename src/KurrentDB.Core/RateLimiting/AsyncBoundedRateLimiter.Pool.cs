// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Threading;
using DotNext.Threading.Tasks;

namespace KurrentDB.Core.RateLimiting;

partial class AsyncBoundedRateLimiter {
	private readonly int _maxPoolSize;
	private int _poolSize;
	private WaitNode _poolHead;

	private void BackToPool(WaitNode node) {
		lock (_syncRoot) {
			BackToPoolUnsafe(node);
		}
	}

	private void BackToPoolUnsafe(WaitNode node) {
		Debug.Assert(Monitor.IsEntered(_syncRoot));
		Debug.Assert(node.Status is ManualResetCompletionSourceStatus.WaitForActivation);
		Debug.Assert(node is { Next: null, Previous: null });

		// Skip the node if the pool is full. The node can be reclaimed by the GC.
		if (_poolSize < _maxPoolSize) {
			node.Next = _poolHead;
			_poolHead = node;
			_poolSize++;
		}
	}

	private WaitNode RentNode() {
		Debug.Assert(Monitor.IsEntered(_syncRoot));

		WaitNode node;
		if (_poolHead is null) {
			node = new();
		} else {
			node = _poolHead;
			_poolHead = node.Next;
			node.Next = null;
			_poolSize--;
		}

		Debug.Assert(node is { Next: null, Previous: null });
		return node;
	}
}
