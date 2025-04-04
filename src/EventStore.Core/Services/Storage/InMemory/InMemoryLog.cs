// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;

namespace EventStore.Core.Services.Storage.InMemory;

// does not support $all style reads, but tracks the LastCommitPosition so that
// the long poll mechanism works in the SubscriptionsService.
// note that the SubscriptionsService currently only supports one physical log
// and one inmemory log so we can't have separate inmemory logs per inmemory stream.
public class InMemoryLog {
	long _lastCommitPosition = 0L;

	public long GetLastCommitPosition() => Interlocked.Read(ref _lastCommitPosition);
	public long GetNextCommitPosition() => Interlocked.Increment(ref _lastCommitPosition);
}
