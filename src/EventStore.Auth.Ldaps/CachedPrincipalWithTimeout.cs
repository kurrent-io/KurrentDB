// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;

namespace EventStore.Auth.Ldaps;

internal class CachedPrincipalWithTimeout {
	public readonly string Password;
	public readonly ClaimsPrincipal Principal;
	public readonly DateTime ValidUntil;

	public CachedPrincipalWithTimeout(string password, ClaimsPrincipal principal, DateTime validUntil) {
		Password = password;
		Principal = principal;
		ValidUntil = validUntil;
	}
}
