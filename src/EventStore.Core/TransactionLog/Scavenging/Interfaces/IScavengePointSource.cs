// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IScavengePointSource {
	// returns null when no scavenge point
	Task<ScavengePoint> GetLatestScavengePointOrDefaultAsync(CancellationToken cancellationToken);
	Task<ScavengePoint> AddScavengePointAsync(
		long expectedVersion,
		int threshold,
		CancellationToken cancellationToken);
}
