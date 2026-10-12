// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.CodeAnalysis;
using DotNext;
using EventStore.Plugins.Authorization;

namespace KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class LegacyAuthorizationWithStreamAuthorizationDisabledPlugin : IAuthorizationPlugin {
	public const string Name = "LegacyAuthorizationWithStreamAuthorizationDisabled";
	private const string FeatureName = $"{IAuthorizationPlugin.FeatureNamePrefix}.{Name}";

	[FeatureSwitchDefinition(FeatureName)]
	public static bool IsAllowed { get; } = AppContext.IsFeatureSupported(FeatureName);

	public IAuthorizationProviderFactory GetAuthorizationProviderFactory(string authorizationConfigPath) =>
		new LegacyAuthorizationWithStreamAuthorizationDisabledProviderFactory();

	string IAuthorizationPlugin.Name => Name;

	public string Version { get; } =
		typeof(LegacyAuthorizationWithStreamAuthorizationDisabledPlugin).Assembly.GetName().Version!.ToString();

	public string CommandLineName { get; } = "legacy-authorization-with-stream-authorization-disabled";
}
