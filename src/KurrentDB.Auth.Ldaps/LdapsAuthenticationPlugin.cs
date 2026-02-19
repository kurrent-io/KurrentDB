// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel.Composition;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Auth.Ldaps;

[Export(typeof(IAuthenticationPlugin))]
public class LdapsAuthenticationPlugin(
	IConfiguration configuration,
	string configFileKey,
	ILoggerFactory loggerFactory) : IAuthenticationPlugin {

	public string Name { get { return "LDAPS"; } }

	public string Version {
		get { return typeof(LdapsAuthenticationPlugin).Assembly.GetName().Version.ToString(); }
	}

	public string CommandLineName { get { return "ldaps"; } }

	public IAuthenticationProviderFactory GetAuthenticationProviderFactory(string _) {
		var logger = loggerFactory.CreateLogger<LdapsAuthenticationPlugin>();

		var ldapsSettings = new ConfigParser(logger)
			.ReadConfiguration<LdapsSettings>(configuration, configFileKey, "LdapsAuth");

		return new LdapsAuthenticationProviderFactory(ldapsSettings);
	}
}
