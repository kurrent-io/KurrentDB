// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> {
	private readonly AsyncCounter _rateLimiter; // can be null if rate limit is not defined

	private ValueTask AcquireRateLimitLeaseAsync(CancellationToken token)
		=> _rateLimiter?.WaitAsync(token) ?? ValueTask.CompletedTask;

	private void ReleaseRateLimitLease() => _rateLimiter?.Increment();
}
