// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.InMemory;

public class InMemoryMetastreamScavengeMap<TKey> :
	InMemoryScavengeMap<TKey, MetastreamData>,
	IMetastreamScavengeMap<TKey> {

	public void SetTombstone(TKey key) {
		if (!TryGetValue(key, out var x))
			x = new MetastreamData();

		this[key] = new MetastreamData(
			isTombstoned: true,
			discardPoint: x.DiscardPoint);
	}

	public void SetDiscardPoint(TKey key, DiscardPoint discardPoint) {
		if (!TryGetValue(key, out var x))
			x = new MetastreamData();

		this[key] = new MetastreamData(
			isTombstoned: x.IsTombstoned,
			discardPoint: discardPoint);
	}

	public void DeleteAll() {
		foreach (var kvp in AllRecords()) {
			TryRemove(kvp.Key, out _);
		}
	}
}
