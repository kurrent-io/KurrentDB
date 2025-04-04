// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

internal static class WellKnownAssertions {
	public static readonly IAssertion System = new MultipleClaimMatchAssertion(Grant.Allow, MultipleMatchMode.All,
		new Claim(ClaimTypes.Name, "system"), new Claim(ClaimTypes.Authentication, "ES-Legacy"));

	public static readonly IAssertion Admin = new MultipleClaimMatchAssertion(Grant.Allow, MultipleMatchMode.Any,
		new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, SystemRoles.Admins));
}
