// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration.Sources.Legacy;

public class EventStoreDefaultLocationsSourceTests {
	[Fact]
	public void ContainsNoConfigurationIfNoFilesOrDirectoriesAreFound() {
		// Arrange
		var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>());
		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyDefaultLocations(LocationOptionWithLegacyDefault.SupportedLegacyLocations, fileSystem)
			.Build();

		// Assert
		configuration.AsEnumerable().Should().BeEmpty();
	}

	[Theory]
	[InlineData(new []{"kurrentdb.conf"}, null)]
	[InlineData(new []{"kurrentdb.conf", "eventstore.conf"}, null)]
	[InlineData(new []{"eventstore.conf"}, "eventstore.conf")]
	[InlineData(new string[]{}, null)]
	public void ProvidesDefaultConfigPath(string[] existingFiles, string? expected) {
		// Arrange
		var mockFiles =
			existingFiles.ToDictionary(x => x, _ => new MockFileData("config"));
		var fileSystem = new MockFileSystem(mockFiles);

		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyDefaultLocations([new LocationOptionWithLegacyDefault(
					$"{KurrentConfigurationKeys.Prefix}:Config",
					"kurrentdb.conf",
					"eventstore.conf",
					true)], fileSystem)
			.Build();

		// Assert
		var configPath = configuration.GetValue<string>($"{KurrentConfigurationKeys.Prefix}:Config");
		configPath.Should().Be(expected);
	}

	[Theory]
	[InlineData(new []{"/kurrentdb_data/"}, null)]
	[InlineData(new []{"/kurrentdb_data/", "/eventstore_data/"}, null)]
	[InlineData(new []{"/eventstore_data/"}, "/eventstore_data/")]
	[InlineData(new string[]{}, null)]
	public void ProvidesDefaultDataPath(string[] existingDirs, string? expected) {
		// Arrange
		var fileSystem = new MockFileSystem();
		foreach (var dir in existingDirs) {
			fileSystem.AddDirectory(dir);
		}

		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyDefaultLocations([new LocationOptionWithLegacyDefault(
				$"{KurrentConfigurationKeys.Prefix}:Db",
				"/kurrentdb_data/",
				"/eventstore_data/",
				false)], fileSystem)
			.Build();

		// Assert
		var configPath = configuration.GetValue<string>($"{KurrentConfigurationKeys.Prefix}:Db");
		configPath.Should().Be(expected);
	}

	[Theory]
	[InlineData(new []{"/kurrentdb_log/"}, null)]
	[InlineData(new []{"/kurrentdb_log/", "/eventstore_log/"}, null)]
	[InlineData(new []{"/eventstore_log/"}, "/eventstore_log/")]
	[InlineData(new string[]{}, null)]
	public void ProvidesDefaultLogPath(string[] existingDirs, string? expected) {
		// Arrange
		var fileSystem = new MockFileSystem();
		foreach (var dir in existingDirs) {
			fileSystem.AddDirectory(dir);
		}

		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyDefaultLocations([new LocationOptionWithLegacyDefault(
				$"{KurrentConfigurationKeys.Prefix}:Log",
				"/kurrentdb_log/",
				"/eventstore_log/",
				false)], fileSystem)
			.Build();

		// Assert
		var configPath = configuration.GetValue<string>($"{KurrentConfigurationKeys.Prefix}:Log");
		configPath.Should().Be(expected);
	}
}
