// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class LegacyAuthorizationWithStreamAuthorizationDisabledPlugin : IAuthorizationPlugin {
	public IAuthorizationProviderFactory GetAuthorizationProviderFactory(string authorizationConfigPath) =>
		new LegacyAuthorizationWithStreamAuthorizationDisabledProviderFactory();

	public string Name { get; } = "LegacyAuthorizationWithStreamAuthorizationDisabled";

	public string Version { get; } =
		typeof(LegacyAuthorizationWithStreamAuthorizationDisabledPlugin).Assembly.GetName().Version!.ToString();

	public string CommandLineName { get; } = "legacy-authorization-with-stream-authorization-disabled";
}
