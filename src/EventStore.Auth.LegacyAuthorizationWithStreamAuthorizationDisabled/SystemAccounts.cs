// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public static class SystemAccounts {
	private static readonly IReadOnlyList<Claim> Claims = new[] {
		new Claim(ClaimTypes.Name, "system"),
		new Claim(ClaimTypes.Role, "system"),
		new Claim(ClaimTypes.Role, SystemRoles.Admins),
	};

	public static readonly ClaimsPrincipal System = new ClaimsPrincipal(new ClaimsIdentity(Claims, "system"));

	public static readonly ClaimsPrincipal Anonymous =
		new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.Anonymous, ""),}));
}
