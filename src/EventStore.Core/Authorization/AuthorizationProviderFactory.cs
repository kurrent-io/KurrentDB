// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class AuthorizationProviderFactory {
	private readonly Func<AuthorizationProviderFactoryComponents, IAuthorizationProviderFactory>
		_authorizationProviderFactory;

	public AuthorizationProviderFactory(
		Func<AuthorizationProviderFactoryComponents, IAuthorizationProviderFactory>
			authorizationProviderFactory) {
		_authorizationProviderFactory = authorizationProviderFactory;
	}

	public IAuthorizationProviderFactory GetFactory(
		AuthorizationProviderFactoryComponents authorizationProviderFactoryComponents) =>
		_authorizationProviderFactory(authorizationProviderFactoryComponents);
}
