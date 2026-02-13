// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Auth.Ldaps;

internal class LdapsAuthenticationProviderFactory(string configPath, ILogger logger) : IAuthenticationProviderFactory {
	public const int CachedPrincipalCount = 1000;

	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) {
		var ldapsSettings = ConfigParser.ReadConfiguration<LdapsSettings>(configPath, "LdapsAuth", logger);
		var authenticationProvider = new LdapsAuthenticationProvider(ldapsSettings,
			CachedPrincipalCount, logFailedAuthenticationAttempts);
		return authenticationProvider;
	}
}
