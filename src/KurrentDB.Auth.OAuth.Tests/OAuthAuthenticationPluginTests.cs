// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using EventStore.Plugins.Licensing;
using KurrentDB.Plugins.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KurrentDB.Auth.OAuth.Tests;

public class OAuthAuthenticationPluginTests {
	private const string ConfigFileKey = "AuthenticationConfig";
	private readonly IConfiguration _configuration;

	public OAuthAuthenticationPluginTests() {
		_configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string> {
				[$"KurrentDB:{ConfigFileKey}"] = Path.Combine(Environment.CurrentDirectory, "conf", "oauth.conf"),
			})
			.Build();
	}

	[Theory]
	[InlineData(true, "OAUTH_AUTHENTICATION", false)]
	[InlineData(true, "NONE", true)]
	[InlineData(false, "NONE", true)]
	public void respects_license(bool licensePresent, string entitlement, bool expectedException) {
		// given
		var sut = new OAuthAuthenticationPlugin(_configuration, ConfigFileKey, NullLoggerFactory.Instance)
			.GetAuthenticationProviderFactory("")
			.Build(false);

		var config = new ConfigurationBuilder().Build();
		var builder = WebApplication.CreateBuilder();

		var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		sut.ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		sut.ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}
}
