// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Authentication;

public interface IAuthenticationProviderFactory {
	/// <summary>
	///     Build an AuthenticationProvider for the authentication plugin
	/// </summary>
	/// <param name="logFailedAuthenticationAttempts">
	///     Whether the Authentication Provider should log failed authentication attempts
	/// </param>
	IAuthenticationProvider Build(bool logFailedAuthenticationAttempts);
}
