// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services.Archive;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive;

public class ArchivePlugableComponentTests {
	[Theory]
	[InlineData(true, true, "ARCHIVE", false)]
	[InlineData(true, false, "ARCHIVE", true)]
	[InlineData(false, true, "ARCHIVE", false)]
	[InlineData(false, false, "ARCHIVE", false)]
	[InlineData(true, true, "NONE", true)]
	[InlineData(true, false, "NONE", true)]
	[InlineData(false, true, "NONE", false)]
	[InlineData(false, false, "NONE", false)]
	public void respects_license(bool enabled, bool licensePresent, string entitlement, bool expectedException) {
		// given
		var sut = new ArchivePlugableComponent(isArchiver: false);

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();

		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string> {
				{$"{KurrentConfigurationKeys.Prefix}:Archive:Enabled", "true"},
			});

		var config = configBuilder.Build();

		var builder = WebApplication.CreateBuilder();

		var licenseService = new FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);
		builder.Services.AddSingleton<IReadOnlyList<IClusterVNodeStartupTask>>([]);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}
}
