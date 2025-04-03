// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;

namespace EventStore.Common.Utils;

public static class ClaimsPrincipalExtensions {
	public static bool LegacyRoleCheck(this ClaimsPrincipal user, string role)
	{
		return user.HasClaim(ClaimTypes.Name, role) || user.HasClaim(ClaimTypes.Role, role);
	}
}
