// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.DataStructures;

public class NoLRUCache<TKey, TValue> : ILRUCache<TKey, TValue> {
	public string Name => "No Cache";
	public long Size => 0;
	public long Count => 0;
	public long FreedSize => 0;
	public long Capacity { get; private set; }

	public void SetCapacity(long value) {
		Capacity = value;
	}

	public void ResetFreedSize() {
	}

	public void Clear() {
	}

	public bool TryGet(TKey key, out TValue value) {
		value = default(TValue);
		return false;
	}

	public TValue Put(TKey key, TValue value) {
		return value;
	}

	public TValue Put<T>(TKey key, T userData, Func<TKey, T, TValue> addFactory,
		Func<TKey, TValue, T, TValue> updateFactory) {
		return addFactory(key, userData);
	}
}
