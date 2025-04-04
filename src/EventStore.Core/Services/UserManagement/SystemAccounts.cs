// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;

namespace EventStore.Core.Services.UserManagement;

public class SystemAccounts {
	private static readonly IReadOnlyList<Claim> Claims = new[] {
		new Claim(ClaimTypes.Name, "system"),
		new Claim(ClaimTypes.Role, "system"),
		new Claim(ClaimTypes.Role, SystemRoles.Admins),
	};
	public static readonly ClaimsPrincipal System = new ClaimsPrincipal(new ClaimsIdentity(Claims, "system"));
	public static readonly ClaimsPrincipal Anonymous = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] { new Claim(ClaimTypes.Anonymous, ""), }));
}
