// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class Policy {
	private readonly Dictionary<OperationDefinition, List<IAssertion>> _assertions;
	private readonly long _version;
	private bool _sealed;

	public Policy(string name, long version, DateTimeOffset validFrom) {
		_version = version;
		Name = name;
		ValidFrom = validFrom;
		_assertions = new Dictionary<OperationDefinition, List<IAssertion>>();
		_sealed = false;
	}

	public string Name { get; }
	public DateTimeOffset ValidFrom { get; }

	public void AddMatchAnyAssertion(OperationDefinition operation, Grant grant, params Claim[] claims) {
		var assertion = new MultipleClaimMatchAssertion(grant, MultipleMatchMode.Any, claims);
		Add(operation, assertion);
	}

	public void AddClaimMatchAssertion(OperationDefinition operation, Grant grant, Claim claim) {
		var assertion = new ClaimMatchAssertion(grant, claim);
		Add(operation, assertion);
	}

	public void Add(OperationDefinition operation, IAssertion assertion) {
		if (_sealed)
			throw new InvalidOperationException("policy has been sealed and no further changes can be made");
		if (!_assertions.TryGetValue(operation, out var assertions))
			_assertions[operation] = assertions = new List<IAssertion>();
		var insertAt = assertions.BinarySearch(assertion, AssertionComparer.Instance);
		if (insertAt >= 0)
			throw new InvalidOperationException("Assertion already exists");
		assertions.Insert(~insertAt, assertion);
	}

	public ReadOnlyPolicy AsReadOnly() {
		_sealed = true;
		return new ReadOnlyPolicy(Name, _version, ValidFrom,
			_assertions
				.ToDictionary<KeyValuePair<OperationDefinition, List<IAssertion>>, OperationDefinition,
					ReadOnlyMemory<IAssertion>>(x => x.Key, x => x.Value.ToArray()));
	}

	public void AllowAnonymous(in OperationDefinition operation) {
		Add(operation, new AllowAnonymousAssertion());
	}

	public void RequireAuthenticated(in OperationDefinition operation) {
		Add(operation, new RequireAuthenticatedAssertion());
	}
}
