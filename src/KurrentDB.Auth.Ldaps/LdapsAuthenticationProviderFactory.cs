// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authentication;

namespace KurrentDB.Auth.Ldaps;

internal class LdapsAuthenticationProviderFactory(LdapsSettings settings) : IAuthenticationProviderFactory {
	public const int CachedPrincipalCount = 1000;

	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) {
		var authenticationProvider = new LdapsAuthenticationProvider(settings,
			CachedPrincipalCount, logFailedAuthenticationAttempts);
		return authenticationProvider;
	}
}
