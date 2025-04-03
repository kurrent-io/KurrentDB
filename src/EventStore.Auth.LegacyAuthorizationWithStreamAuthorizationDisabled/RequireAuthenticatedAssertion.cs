// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

internal class RequireAuthenticatedAssertion : IAssertion {
	public Grant Grant { get; } = Grant.Unknown;

	public AssertionInformation Information { get; } =
		new AssertionInformation("match", "authenticated", Grant.Unknown);

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		if (!cp.Claims.Any() ||
		    cp.Claims.Any(x => string.Equals(x.Type, ClaimTypes.Anonymous, StringComparison.Ordinal)))
			context.Add(new AssertionMatch(policy, new AssertionInformation("match", "authenticated", Grant.Deny)));
		else
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("match", "authenticated", Grant.Allow)));
		return new ValueTask<bool>(true);
	}
}
