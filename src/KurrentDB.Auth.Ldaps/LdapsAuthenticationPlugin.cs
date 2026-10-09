// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.CodeAnalysis;
using DotNext;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Auth.Ldaps;

public class LdapsAuthenticationPlugin(
	IConfiguration configuration,
	string configFileKey,
	ILoggerFactory loggerFactory) : IAuthenticationPlugin {
	public const string Name = "LDAPS";
	private const string FeatureName = $"{IAuthenticationPlugin.FeatureNamePrefix}.{Name}";

	string IAuthenticationPlugin.Name => Name;

	[FeatureSwitchDefinition(FeatureName)]
	public static bool IsAllowed { get; } = AppContext.IsFeatureSupported(FeatureName);

	public string Version {
		get { return typeof(LdapsAuthenticationPlugin).Assembly.GetName().Version?.ToString() ?? string.Empty; }
	}

	public string CommandLineName { get { return "ldaps"; } }

	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification = "LdapsSettings and dependent types are preserved.")]
	public IAuthenticationProviderFactory GetAuthenticationProviderFactory(string _) {
		var logger = loggerFactory.CreateLogger<LdapsAuthenticationPlugin>();

		var ldapsSettings = new ConfigParser(logger)
			.ReadConfiguration<LdapsSettings>(configuration, configFileKey, "LdapsAuth");

		return new LdapsAuthenticationProviderFactory(ldapsSettings);
	}
}
