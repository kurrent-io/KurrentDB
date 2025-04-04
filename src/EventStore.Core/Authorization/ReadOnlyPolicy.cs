// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class ReadOnlyPolicy {
	private readonly IReadOnlyDictionary<OperationDefinition, ReadOnlyMemory<IAssertion>> _assertions;
	private readonly string _name;
	private readonly DateTimeOffset _validFrom;
	private readonly long _version;

	public ReadOnlyPolicy(string name, long version, DateTimeOffset validFrom,
		IReadOnlyDictionary<OperationDefinition, ReadOnlyMemory<IAssertion>> assertions) {
		_name = name;
		_version = version;
		_validFrom = validFrom;
		_assertions = assertions;
	}

	public PolicyInformation Information => new PolicyInformation(_name, _version, DateTimeOffset.MaxValue);

	public bool TryGetAssertions(OperationDefinition operation, out ReadOnlyMemory<IAssertion> assertions) {
		return _assertions.TryGetValue(operation, out assertions);
	}
}
