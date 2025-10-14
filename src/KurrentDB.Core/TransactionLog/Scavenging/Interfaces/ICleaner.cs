// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using KurrentDB.Core.TransactionLog.Scavenging.Data;

namespace KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

public interface ICleaner {
	void Clean(
		ScavengePoint scavengePoint,
		IScavengeStateForCleaner state,
		CancellationToken cancellationToken);

	void Clean(
		ScavengeCheckpoint.Cleaning checkpoint,
		IScavengeStateForCleaner state,
		CancellationToken cancellationToken);
}
