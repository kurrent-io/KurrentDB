// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Authentication;

namespace EventStore.Auth.Ldaps;

internal class LdapsAuthenticationProviderFactory : IAuthenticationProviderFactory {
	public const int CachedPrincipalCount = 1000;
	private readonly string _configPath;
	private LdapsAuthenticationProvider _authenticationProvider;

	public LdapsAuthenticationProviderFactory(string configPath) {
		_configPath = configPath;
	}

	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) {
		var ldapsSettings = ConfigParser.ReadConfiguration<LdapsSettings>(_configPath, "LdapsAuth");
		_authenticationProvider = new LdapsAuthenticationProvider(ldapsSettings,
			CachedPrincipalCount, logFailedAuthenticationAttempts);
		return _authenticationProvider;
	}
}
