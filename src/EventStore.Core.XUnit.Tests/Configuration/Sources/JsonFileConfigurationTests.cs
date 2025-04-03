// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class JsonFileConfigurationTests {
	[Fact]
	public void LoadsConfigFromKurrentJsonFile() {
		// Arrange
		var fileName = "test.kurrentdb.json";
		var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", fileName);
		var directory = Path.GetDirectoryName(filePath);
		Assert.NotNull(directory);

		// Act
		var config = new ConfigurationBuilder()
			.AddJsonFile(config => {
				config.FileProvider = new PhysicalFileProvider(directory) {
					UseActivePolling = false,
					UsePollingFileWatcher = false
				};
				config.OnLoadException = _ =>
					Assert.Fail($"Could not find test config file '{fileName}' in '{directory}'");
				config.Path = fileName;
			}).Build();

		// Assert
		config.GetValue<bool>($"{KurrentConfigurationKeys.Prefix}:AutoScavenge:Enabled").Should().Be(false);
		config.GetValue<string>($"{KurrentConfigurationKeys.Prefix}:Licensing:LicenseKey").Should().Be("valid");
	}
}
