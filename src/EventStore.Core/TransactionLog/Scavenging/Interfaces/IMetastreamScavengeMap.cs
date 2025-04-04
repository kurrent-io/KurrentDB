// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IMetastreamScavengeMap<TKey> :
	IScavengeMap<TKey, MetastreamData> {

	void SetTombstone(TKey key);

	void SetDiscardPoint(TKey key, DiscardPoint discardPoint);

	void DeleteAll();
}
