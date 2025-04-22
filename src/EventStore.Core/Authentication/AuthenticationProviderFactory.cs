// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Plugins.Authentication;

namespace EventStore.Core.Authentication;

public class AuthenticationProviderFactory {
	private readonly Func<AuthenticationProviderFactoryComponents, IAuthenticationProviderFactory>
		_authenticationProviderFactory;


	public AuthenticationProviderFactory(
		Func<AuthenticationProviderFactoryComponents, IAuthenticationProviderFactory>
			authenticationProviderFactory) {
		_authenticationProviderFactory = authenticationProviderFactory;
	}

	public IAuthenticationProviderFactory GetFactory(
		AuthenticationProviderFactoryComponents authenticationProviderFactoryComponents) =>
		_authenticationProviderFactory(authenticationProviderFactoryComponents);
}
