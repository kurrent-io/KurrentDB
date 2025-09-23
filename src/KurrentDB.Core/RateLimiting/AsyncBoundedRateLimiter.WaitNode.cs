// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNext.Threading.Tasks;

namespace KurrentDB.Core.RateLimiting;

partial class AsyncBoundedRateLimiter {
	private static readonly object Sentinel = new();

	/// <summary>
	/// Represents the suspended caller.
	/// </summary>
	private sealed class WaitNode: ValueTaskCompletionSource<bool> {
		private AsyncBoundedRateLimiter _owner;
		private bool _prioritized;
		private WaitNode _previous;

		// Reused by the queue and the pool
		public WaitNode Next;

		public WaitNode Previous => _previous;

		/// <summary>
		/// <see cref="Sentinel"/> object is used to fill <see cref="ManualResetCompletionSource.CompletionData"/>
		/// when we need to distinguish two completion reasons:
		/// 1. Completed by cancellation token. We need to remove the node manually from the queue
		/// 2. Completed normally by signaling thread. The node is removed by the thread.
		/// </summary>
		public bool NeedsRemoval => CompletionData is null;

		public void Initialize(AsyncBoundedRateLimiter owner, bool prioritized) {
			_prioritized = prioritized;
			_owner = owner;
		}

		public bool IsPrioritized => _prioritized;

		protected override void CleanUp() {
			_prioritized = false;
			_owner = null;
			base.CleanUp();
		}

		protected override void AfterConsumed() => _owner?.ReturnNode(this);

		public void Append(WaitNode node) {
			Debug.Assert(node.Next is null);

			Next = node;
			node._previous = this;
		}

		public void Detach() {
			if (_previous is not null)
				_previous.Next = Next;

			if (Next is not null)
				Next._previous = _previous;

			Next = _previous = null;
		}

		// resumable flag indicates that the node has the registered consumer, e.g. suspended caller
		// that awaits on ValueTask<bool> produced by this source. If there is a consumer,
		// we can resume it later out of the main lock by using NotifyConsumer() method
		public bool TrySignal(out bool resumable)
			=> TrySetResult(completionData: Sentinel, completionToken: null, result: true, out resumable);

		public new void NotifyConsumer() {
			Debug.Assert(!NeedsRemoval);
			Debug.Assert(Status is ManualResetCompletionSourceStatus.WaitForConsumption);

			base.NotifyConsumer();
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private struct WaitNodeList {
		private WaitNode _first, _last;

		public readonly WaitNode First => _first;

		public void Add(WaitNode node) {
			if (_first is null) {
				_first = _last = node;
			} else {
				Debug.Assert(_last is not null);

				_last.Append(node);
				_last = node;
			}
		}

		public void Remove(WaitNode node) {
			if (ReferenceEquals(_first, node)) {
				_first = node.Next;
			}

			if (ReferenceEquals(_last, node)) {
				_last = node.Previous;
			}

			node.Detach();
		}
	}
}
