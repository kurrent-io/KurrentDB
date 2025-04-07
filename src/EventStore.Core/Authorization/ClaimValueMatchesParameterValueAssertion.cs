// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

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
