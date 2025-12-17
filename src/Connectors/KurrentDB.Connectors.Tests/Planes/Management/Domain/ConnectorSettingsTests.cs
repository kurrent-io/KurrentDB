// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Planes.Management.Domain;

namespace KurrentDB.Connectors.Tests.Planes.Management.Domain;

public class ConnectorSettingsTests {
	[Fact]
	public void should_create_from_dictionary() {
		// Arrange
		var settings = new Dictionary<string, string?> {
			{ "key1", "value1" },
			{ "key2", "value2" }
		};

		// Act
		var result = ConnectorSettings.From(settings, "test-connector");

		// Assert
		result.ConnectorId.Should().Be("test-connector");
		result.Value.Should().HaveCount(2);
		result.Value["key1"].Should().Be("value1");
		result.Value["key2"].Should().Be("value2");
	}

	[Fact]
	public void should_create_from_map_field() {
		// Arrange
		var settings = new Dictionary<string, string?> {
			{ "key1", "value1" },
			{ "key2", "value2" }
		};

		// Act
		var result = ConnectorSettings.From(settings, "test-connector");

		// Assert
		result.ConnectorId.Should().Be("test-connector");
		result.Value.Should().HaveCount(2);
		result.Value["key1"].Should().Be("value1");
		result.Value["key2"].Should().Be("value2");
	}

	[Fact]
	public void should_create_from_struct() {
		// Arrange
		var configuration = new Struct {
			Fields = {
				{ "key1", Value.ForString("value1") },
				{ "nested", Value.ForStruct(new Struct {
					Fields = {
						{ "key2", Value.ForNumber(42) }
					}
				}) }
			}
		};

		// Act
		var result = ConnectorSettings.From(configuration, "test-connector");

		// Assert
		result.ConnectorId.Should().Be("test-connector");
		result.Value.Should().HaveCount(2);
		result.Value["key1"].Should().Be("value1");
		result.Value["nested:key2"].Should().Be("42");
	}

	[Fact]
	public void should_prefer_configuration_over_settings_when_both_provided() {
		// Arrange
		var configuration = new Struct {
			Fields = {
				{ "fromConfig", Value.ForString("configValue") }
			}
		};
		var settings = new Dictionary<string, string?> {
			{ "fromSettings", "settingsValue" }
		};

		// Act
		var result = ConnectorSettings.From(configuration, settings, "test-connector");

		// Assert
		result.Value.Should().ContainKey("fromConfig");
		result.Value.Should().NotContainKey("fromSettings");
	}

	[Fact]
	public void should_use_settings_when_configuration_is_null() {
		// Arrange
		Struct? configuration = null;
		var settings = new Dictionary<string, string?> {
			{ "fromSettings", "settingsValue" }
		};

		// Act
		var result = ConnectorSettings.From(configuration, settings, "test-connector");

		// Assert
		result.Value.Should().ContainKey("fromSettings");
		result.Value["fromSettings"].Should().Be("settingsValue");
	}

	[Fact]
	public void should_use_settings_when_configuration_is_empty() {
		// Arrange
		var configuration = new Struct();
		var settings = new Dictionary<string, string?> {
			{ "fromSettings", "settingsValue" }
		};

		// Act
		var result = ConnectorSettings.From(configuration, settings, "test-connector");

		// Assert
		result.Value.Should().ContainKey("fromSettings");
		result.Value["fromSettings"].Should().Be("settingsValue");
	}

	[Fact]
	public void should_prefer_configuration_over_map_field_when_both_provided() {
		// Arrange
		var configuration = new Struct {
			Fields = {
				{ "fromConfig", Value.ForString("configValue") }
			}
		};
		var settings = new Dictionary<string, string?> {
			{ "fromSettings", "settingsValue" }
		};

		// Act
		var result = ConnectorSettings.From(configuration, settings, "test-connector");

		// Assert
		result.Value.Should().ContainKey("fromConfig");
		result.Value.Should().NotContainKey("fromSettings");
	}

	[Fact]
	public void should_use_map_field_when_configuration_is_null() {
		// Arrange
		Struct? configuration = null;
		var settings = new Dictionary<string, string?> {
			{ "fromSettings", "settingsValue" }
		};

		// Act
		var result = ConnectorSettings.From(configuration, settings, "test-connector");

		// Assert
		result.Value.Should().ContainKey("fromSettings");
		result.Value["fromSettings"].Should().Be("settingsValue");
	}

	[Fact]
	public void should_convert_to_dictionary() {
		// Arrange
		var settings = ConnectorSettings.From(
			new Dictionary<string, string?> {
				{ "key1", "value1" },
				{ "key2", "value2" }
			},
			"test-connector"
		);

		// Act
		var result = settings.AsDictionary();

		// Assert
		result.Should().BeEquivalentTo(settings.Value);
	}

	[Fact]
	public void should_convert_to_configuration() {
		// Arrange
		var settings = ConnectorSettings.From(
			new Dictionary<string, string?> {
				{ "key1", "value1" },
				{ "nested:key2", "value2" }
			},
			"test-connector"
		);

		// Act
		var result = settings.AsConfiguration();

		// Assert
		result["key1"].Should().Be("value1");
		result["nested:key2"].Should().Be("value2");
	}

	[Fact]
	public void should_handle_complex_nested_configuration() {
		// Arrange
		var configuration = new Struct {
			Fields = {
				{ "authentication", Value.ForStruct(new Struct {
					Fields = {
						{ "method", Value.ForString("Bearer") },
						{ "bearer", Value.ForStruct(new Struct {
							Fields = {
								{ "token", Value.ForString("secret") }
							}
						}) }
					}
				}) },
				{ "brokers", Value.ForList(
					Value.ForString("localhost:9092"),
					Value.ForString("localhost:9093")
				)}
			}
		};

		// Act
		var result = ConnectorSettings.From(configuration, "test-connector");

		// Assert
		result.Value.Should().ContainKey("authentication:method");
		result.Value["authentication:method"].Should().Be("Bearer");
		result.Value.Should().ContainKey("authentication:bearer:token");
		result.Value["authentication:bearer:token"].Should().Be("secret");
		result.Value.Should().ContainKey("brokers:0");
		result.Value["brokers:0"].Should().Be("localhost:9092");
		result.Value.Should().ContainKey("brokers:1");
		result.Value["brokers:1"].Should().Be("localhost:9093");
	}

	[Fact]
	public void should_use_case_insensitive_comparison() {
		// Arrange
		var settings = new Dictionary<string, string?> {
			{ "Key1", "value1" },
			{ "key2", "value2" }
		};

		// Act
		var result = ConnectorSettings.From(settings, "test-connector");

		// Assert
		result.Value["key1"].Should().Be("value1"); // Should match "Key1" case-insensitively
		result.Value["KEY2"].Should().Be("value2"); // Should match "key2" case-insensitively
	}
}

