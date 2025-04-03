// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class ClaimMatchAssertion : IComparable<ClaimMatchAssertion>, IAssertion {
	private readonly Claim _claim;

	public ClaimMatchAssertion(Grant grant, Claim claim) {
		_claim = claim;
		Grant = grant;
		Information = new AssertionInformation("equal", _claim.ToString(), grant);
	}

	public AssertionInformation Information { get; }

	public Grant Grant { get; }

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		// ReSharper disable once PatternAlwaysOfType
		if (cp.FindFirst(x =>
			string.Equals(x.Type, _claim.Type, StringComparison.Ordinal) &&
			string.Equals(x.Value, _claim.Value, StringComparison.Ordinal)) is Claim matched) {
			context.Add(new AssertionMatch(policy, Information, matched));
			return new ValueTask<bool>(true);
		}

		return new ValueTask<bool>(false);
	}

	public int CompareTo(ClaimMatchAssertion other) {
		if (other == null)
			throw new ArgumentNullException(nameof(other));

		var grant = Grant.CompareTo(other.Grant);
		if (grant != 0)
			return grant * -1;
		var type = string.CompareOrdinal(_claim.Type, other._claim.Type);
		if (type != 0)
			return type;
		return string.CompareOrdinal(_claim.Value, other._claim.Value);
	}
}
