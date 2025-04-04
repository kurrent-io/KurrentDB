// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authentication;

namespace EventStore.Core.Authentication.PassthroughAuthentication;

public class PassthroughAuthenticationProviderFactory : IAuthenticationProviderFactory {
	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) => new PassthroughAuthenticationProvider();
}
