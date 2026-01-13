// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler {
	internal interface ISynchronizationGroup : IDisposable {
		ValueTask AcquireAsync(CancellationToken token);
		void Release();
	}

	private sealed class SimpleSynchronizer : AsyncExclusiveLock, ISynchronizationGroup;

	private sealed class RateLimitingSynchronizer(long initialLeaseCount) : AsyncCounter(initialLeaseCount), ISynchronizationGroup {
		ValueTask ISynchronizationGroup.AcquireAsync(CancellationToken token)
			=> WaitAsync(token);

		void ISynchronizationGroup.Release() => Increment();
	}
}
