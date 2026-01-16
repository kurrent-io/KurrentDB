// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Testing;
using Shouldly;

namespace KurrentDB.Ammeter;

// typically tests will be in separate test projects related to the projects that they test
// but we can add extra tests directly in ammeter, too
[Property("KurrentCloud", "true")]
public class ConnectorsTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	[Test]
	public async Task can_create_a_connector_with_settings() {
		var connectorId = Guid.NewGuid();
		var connectorName = $"connector-{connectorId}";
		TestContext.Current!.StateBag.Items["connectorName"] = connectorName;
		var sourceStream = $"stream-{connectorId}";
		var targetUrl = "http://test.com";

		await KurrentContext.ConnectorsCommandServiceClient.CreateAsync(new() {
			ConnectorId = connectorName,
			Name = connectorName,
			Settings = {
				{ "authentication:method", "Bearer" },
				{ "authentication:bearer:token", "the-secret" },
				{ "instanceTypeName", "http-sink" },
				{ "subscription:filter:scope", "stream" },
				{ "subscription:filter:filterType", "streamId" },
				{ "subscription:filter:expression", sourceStream },
				{ "url", targetUrl },
			}
		});
	}

	[Test]
	public async Task can_create_a_connector_with_configuration() {
		var connectorId = Guid.NewGuid();
		var connectorName = $"connector-{connectorId}";
		TestContext.Current!.StateBag.Items["connectorName"] = connectorName;
		var sourceStream = $"stream-{connectorId}";
		var targetUrl = "http://test.com";

		await KurrentContext.ConnectorsCommandServiceClient.CreateAsync(new() {
			ConnectorId = connectorName,
			Name = connectorName,
			Configuration = new Dictionary<string, string?> {
				["authentication:method"] = "Bearer",
				["authentication:bearer:token"] = "the-secret",
				["instanceTypeName"] = "http-sink",
				["subscription:filter:scope"] = "stream",
				["subscription:filter:filterType"] = "streamId",
				["subscription:filter:expression"] = sourceStream,
				["url"] = targetUrl
			}.ToProtobufStruct()
		});
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_settings))]
	public async Task can_list_connector_with_settings() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_settings))[0]
			.StateBag.Items["connectorName"] as string;

		var result = await KurrentContext.ConnectorsQueryServiceClient.ListAsync(new() {
			IncludeSettings = true
		});

		result.TotalSize.ShouldBeGreaterThanOrEqualTo(1);
		result.Items.ShouldNotBeEmpty();

		var connector = result.Items.FirstOrDefault(c => c.Name == connectorName!);
		connector.ShouldNotBeNull();
		connector.Name.ShouldBe(connectorName);
		connector.Settings["instanceTypeName"].ShouldBe("http-sink");
		connector.Configuration.ShouldBeNull();
		connector.ConfigurationUpdateTime.ShouldBeNull();
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_configuration))]
	public async Task can_list_connector_with_configuration() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_configuration))[0]
			.StateBag.Items["connectorName"] as string;

		var result = await KurrentContext.ConnectorsQueryServiceClient.ListAsync(new() {
			IncludeConfiguration = true
		});

		result.TotalSize.ShouldBeGreaterThanOrEqualTo(1);
		result.Items.ShouldNotBeEmpty();

		var connector = result.Items.FirstOrDefault(c => c.Name == connectorName!);
		connector.ShouldNotBeNull();
		connector.Name.ShouldBe(connectorName);
		connector.Configuration.Fields["instanceTypeName"].StringValue.ShouldBe("http-sink");
		connector.ConfigurationUpdateTime.ShouldNotBeNull();
		connector.Settings.ShouldBeEmpty();
		connector.SettingsUpdateTime.ShouldBeNull();
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_settings))]
	public async Task can_reconfigure_connector_with_settings() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_settings))[0]
			.StateBag.Items["connectorName"] as string;

		var newTargetUrl = "http://reconfigured.com";

		await KurrentContext.ConnectorsCommandServiceClient.ReconfigureAsync(new() {
			ConnectorId = connectorName,
			Settings = {
				{ "authentication:method", "Bearer" },
				{ "authentication:bearer:token", "new-secret" },
				{ "instanceTypeName", "http-sink" },
				{ "subscription:filter:scope", "stream" },
				{ "subscription:filter:filterType", "streamId" },
				{ "subscription:filter:expression", "reconfigured-stream" },
				{ "url", newTargetUrl }
			}
		});

		await Task.Delay(1000);

		var result = await KurrentContext.ConnectorsQueryServiceClient.ListAsync(new() {
			IncludeSettings = true
		});

		var connector = result.Items.FirstOrDefault(c => c.Name == connectorName);
		connector.ShouldNotBeNull();
		connector.Settings["url"].ShouldBe(newTargetUrl);
		connector.Settings["subscription:filter:expression"].ShouldBe("reconfigured-stream");
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_configuration))]
	public async Task can_reconfigure_connector_with_configuration() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_configuration))[0]
			.StateBag.Items["connectorName"] as string;

		var newTargetUrl = "http://reconfigured.com";

		await KurrentContext.ConnectorsCommandServiceClient.ReconfigureAsync(new() {
			ConnectorId = connectorName,
			Configuration = new Dictionary<string, string?> {
				["authentication:method"] = "Bearer",
				["authentication:bearer:token"] = "new-secret",
				["instanceTypeName"] = "http-sink",
				["subscription:filter:scope"] = "stream",
				["subscription:filter:filterType"] = "streamId",
				["subscription:filter:expression"] = "reconfigured-stream",
				["url"] = newTargetUrl
			}.ToProtobufStruct()
		});

		await Task.Delay(1000);

		var result = await KurrentContext.ConnectorsQueryServiceClient.ListAsync(new() {
			IncludeConfiguration = true
		});

		var connector = result.Items.FirstOrDefault(c => c.Name == connectorName);
		connector.ShouldNotBeNull();
		connector.Configuration.Fields["url"].StringValue.ShouldBe(newTargetUrl);
		connector.Configuration.Fields["subscription"]
			.StructValue.Fields["filter"]
			.StructValue.Fields["expression"].StringValue
			.ShouldBe("reconfigured-stream");
	}

	[Test]
	[DependsOn(nameof(can_reconfigure_connector_with_settings))]
	public async Task can_delete_a_connector() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_settings))[0]
			.StateBag.Items["connectorName"];

		await KurrentContext.ConnectorsCommandServiceClient.DeleteAsync(new() {
			ConnectorId = $"{connectorName}"
		});
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_settings))]
	public async Task can_view_settings() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_settings))[0]
			.StateBag.Items["connectorName"];

		var result = await KurrentContext.ConnectorsQueryServiceClient.GetSettingsAsync(new() {
			ConnectorId = $"{connectorName}"
		});

		result.Settings["instanceTypeName"].ShouldBe("http-sink");
		result.SettingsUpdateTime.ShouldNotBeNull();
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector_with_configuration))]
	public async Task can_view_configuration() {
		var connectorName = TestContext.Current!.Dependencies.GetTests(nameof(can_create_a_connector_with_configuration))[0]
			.StateBag.Items["connectorName"];

		var result = await KurrentContext.ConnectorsQueryServiceClient.GetConfigurationAsync(new() {
			ConnectorId = $"{connectorName}"
		});

		result.Configuration.Fields["instanceTypeName"].StringValue.ShouldBe("http-sink");
		result.ConfigurationUpdateTime.ShouldNotBeNull();
	}
}
