// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging;

public static class ScavengeStateExtensions {
	public static void SetCheckpoint(
		this IScavengeStateCommon state,
		ScavengeCheckpoint checkpoint) {

		var transaction = state.BeginTransaction();
		try {
			transaction.Commit(checkpoint);
		} catch {
			transaction.Rollback();
			throw;
		}
	}
}
