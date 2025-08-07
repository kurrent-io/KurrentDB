// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Authentication;

public interface IAuthenticationPlugin {
	string Name { get; }
	string Version { get; }
	string CommandLineName { get; }

	/// <summary>
	///     Creates an authentication provider factory for the authentication plugin
	/// </summary>
	/// <param name="authenticationConfigPath">The path to the configuration file for the authentication plugin</param>
	IAuthenticationProviderFactory GetAuthenticationProviderFactory(string authenticationConfigPath);
}
