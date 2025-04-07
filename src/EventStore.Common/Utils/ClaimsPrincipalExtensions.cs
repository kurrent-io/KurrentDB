// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Security.Claims;

namespace EventStore.Common.Utils;

public static class ClaimsPrincipalExtensions {
	public static bool LegacyRoleCheck(this ClaimsPrincipal user, string role) {
		return user.HasClaim(ClaimTypes.Name, role) || user.HasClaim(ClaimTypes.Role, role);
	}
}
