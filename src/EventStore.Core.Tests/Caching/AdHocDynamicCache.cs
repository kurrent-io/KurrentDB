// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Caching;

namespace EventStore.Core.Tests.Caching;

public class AdHocDynamicCache : IDynamicCache {
	private readonly Func<long> _getSize;
	private readonly Action<long> _setCapacity;
	private readonly Func<long> _getFreedSize;
	private readonly Action _resetFreedSize;

	public AdHocDynamicCache(
		Func<long> getSize,
		Action<long> setCapacity,
		Func<long> getFreedSize = null,
		Action resetFreedSize = null,
		string name = null) {

		_getSize = getSize;
		_setCapacity = setCapacity;
		_getFreedSize = getFreedSize ?? (() => 0);
		_resetFreedSize = resetFreedSize ?? (() => { });

		Name = name ?? nameof(AdHocDynamicCache);
	}

	public string Name { get; }

	public long Capacity { get; private set; }

	public void SetCapacity(long value) {
		Capacity = value;
		_setCapacity(value);
	}

	public void ResetFreedSize() {
		_resetFreedSize();
	}

	public long Size => _getSize();
	public long FreedSize => _getFreedSize();
	public long Count => _getSize(); // one byte per item
}
