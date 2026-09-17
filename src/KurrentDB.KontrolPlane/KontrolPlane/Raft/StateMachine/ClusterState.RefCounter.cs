// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

partial class ClusterState {
	private ulong _referenceCounter;

	public bool TryAcquire() {
		var current = _referenceCounter;
		for (ulong tmp;; current = tmp) {
			if (current is 0UL)
				break;

			tmp = Interlocked.CompareExchange(ref _referenceCounter, current + 1UL, current);
			if (tmp == current)
				break;
		}

		return current > 0UL;
	}

	public void Release() {
		var current = _referenceCounter;
		for (ulong tmp;; current = tmp) {
			if (current is 0UL)
				break;

			tmp = Interlocked.CompareExchange(ref _referenceCounter, current - 1UL, current);
			if (tmp == current)
				break;
		}

		if (current is 1UL) {
			Dispose();
		}
	}
}
