// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Patterns;

namespace KurrentDB.Core.RateLimiting;

partial class AsyncBoundedRateLimiter {
	// produces the canceled task with the associated canceled token
	private sealed class CanceledTaskFactory :
		ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>,
		ISingleton<CanceledTaskFactory> {
		public static CanceledTaskFactory Instance { get; } = new();

		private CanceledTaskFactory() {
		}

		ValueTask<bool> ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>.Invoke(TimeSpan timeout,
			CancellationToken token)
			=> ValueTask.FromCanceled<bool>(token);
	}

	// produces synchronously and successfully completed task with 'true' result
	private sealed class CompletedTaskFactory :
		ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>,
		ISingleton<CompletedTaskFactory> {
		public static CompletedTaskFactory Instance { get; } = new();

		private CompletedTaskFactory() {
		}

		ValueTask<bool> ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>.Invoke(TimeSpan timeout,
			CancellationToken token)
			=> ValueTask.FromResult(true);
	}

	// produces synchronously and successfully completed task with 'false' result
	private sealed class RejectedTaskFactory :
		ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>,
		ISingleton<RejectedTaskFactory> {
		public static RejectedTaskFactory Instance { get; } = new();

		private RejectedTaskFactory() {
		}

		ValueTask<bool> ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>.Invoke(TimeSpan timeout,
			CancellationToken token)
			=> ValueTask.FromResult(false);
	}

	// produces synchronously completed task with ObjectDisposedException
	private sealed class DisposedTaskFactory :
		ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>,
		ISingleton<DisposedTaskFactory> {
		public static DisposedTaskFactory Instance { get; } = new();

		private DisposedTaskFactory() {
		}

		ValueTask<bool> ISupplier<TimeSpan, CancellationToken, ValueTask<bool>>.Invoke(TimeSpan timeout,
			CancellationToken token)
			=> ValueTask.FromException<bool>(new ObjectDisposedException(nameof(AsyncBoundedRateLimiter)));
	}
}
