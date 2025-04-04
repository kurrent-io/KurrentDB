// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.LogAbstraction.Common;

public class NameExistenceFilterValueLookupDecorator<TValue> : IValueLookup<TValue> {
	private readonly IValueLookup<TValue> _wrapped;
	private readonly INameExistenceFilter _existenceFilter;

	public NameExistenceFilterValueLookupDecorator(
		IValueLookup<TValue> wrapped,
		INameExistenceFilter existenceFilter) {

		_wrapped = wrapped;
		_existenceFilter = existenceFilter;
	}

	public TValue LookupValue(string name) {
		if (_existenceFilter.MightContain(name))
			return _wrapped.LookupValue(name);

		return default;
	}
}
