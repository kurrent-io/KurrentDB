// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
