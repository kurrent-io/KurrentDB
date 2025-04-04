// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IScavengeMap<TKey, TValue> {
	bool TryGetValue(TKey key, out TValue value);
	TValue this[TKey key] { set; }
	bool TryRemove(TKey key, out TValue value);
	IEnumerable<KeyValuePair<TKey, TValue>> AllRecords();
}
