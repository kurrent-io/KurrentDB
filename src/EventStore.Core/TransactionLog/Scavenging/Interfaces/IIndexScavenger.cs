// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IIndexScavenger {
	ValueTask ScavengeIndex(
		long scavengePoint,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken cancellationToken);
}
