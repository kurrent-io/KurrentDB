// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class ClaimValueMatchesParameterValueAssertion : IAssertion {
	private readonly string _claimType;
	private readonly string _parameterName;

	public ClaimValueMatchesParameterValueAssertion(string claimType, string parameterName, Grant grant) {
		_claimType = claimType;
		_parameterName = parameterName;
		Grant = grant;
		Information = new AssertionInformation("match", $"{_claimType} : {_parameterName}", Grant);
	}

	public AssertionInformation Information { get; }
	public Grant Grant { get; }

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		if (cp.FindFirst(_claimType) is Claim matchedClaim &&
		    operation.Parameters.Span.Contains(new Parameter(_parameterName, matchedClaim.Value))) {
			context.Add(new AssertionMatch(policy, Information, matchedClaim));
			return new ValueTask<bool>(true);
		}

		return new ValueTask<bool>(false);
	}
}
