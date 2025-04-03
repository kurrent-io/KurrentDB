// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization.AuthorizationPolicies;

/// <summary>
/// This policy selector is used if there are entries in the settings stream
/// but none of them are valid or can be started.
/// Only admins have stream access to prevent falling back to a more permissive policy.
/// </summary>
public class FallbackStreamAccessPolicySelector : IPolicySelector {
	public const string FallbackPolicyName = "system-fallback";

	public ReadOnlyPolicy Select() {
		var restrictedAccess = new RestrictedAccessAssertion();
		var policy = new Policy(FallbackPolicyName, 1, DateTimeOffset.MinValue);
		// Streams
		policy.Add(Operations.Streams.Read, restrictedAccess);
		policy.Add(Operations.Streams.Write, restrictedAccess);
		policy.Add(Operations.Streams.Delete, restrictedAccess);
		policy.Add(Operations.Streams.MetadataRead, restrictedAccess);
		policy.Add(Operations.Streams.MetadataWrite, restrictedAccess);
		// Persistent Subscriptions
		policy.Add(Operations.Subscriptions.ProcessMessages, restrictedAccess);
		return policy.AsReadOnly();
	}
}

public class RestrictedAccessAssertion : IStreamPermissionAssertion {
	public Grant Grant => Grant.Deny;
	public AssertionInformation Information { get; } = new("stream", "restricted access", Grant.Deny);

	public async ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		if (await WellKnownAssertions.System.Evaluate(cp, operation, policy, context) ||
		    await WellKnownAssertions.Admin.Evaluate(cp, operation, policy, context))
			return true;
		context.Add(new AssertionMatch(policy,
			new AssertionInformation("stream", $"operation {operation} restricted to system or admin users only", Grant.Deny)));
		return true;
	}
}
