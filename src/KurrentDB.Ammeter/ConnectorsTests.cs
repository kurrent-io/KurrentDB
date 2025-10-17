// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Testing;
using RestSharp;

namespace KurrentDB.Ammeter;

// typically tests will be in separate test projects related to the projects that they test
// but we can add extra tests directly in ammeter, too
[Property("KurrentCloud", "true")]
public sealed class ConnectorsTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerAssembly)]
	public required KurrentContext Fixture { get; init; }

	[Test]
	public async Task can_create_a_connector() {
		var connectorId = Guid.NewGuid();
		var connectorName = $"connector-{connectorId}";
		TestContext.Current!.ObjectBag.Add("connectorName", connectorName);
		var sourceStream = $"stream-{connectorId}";
		var targetUrl = "http://test.com";

		await Fixture.ConnectorsClient.CreateAsync(new() {
			ConnectorId = connectorName,
			Name = connectorName,
			Settings = {
				{ "authentication:method", "Bearer"} ,
				{ "authentication:bearer:token", "the-secret" },
				{ "instanceTypeName", "http-sink"},
				{ "subscription:filter:scope", "stream"} ,
				{ "subscription:filter:filterType", "streamId"} ,
				{ "subscription:filter:expression", sourceStream},
				{ "url", targetUrl} ,
			}
		});
	}

	[Test]
	[DependsOn(nameof(can_create_a_connector))]
	public async Task can_delete_a_connector() {
		var connectorName = TestContext.Current!.GetTests(nameof(can_create_a_connector)).First().ObjectBag["connectorName"];

		await Fixture.ConnectorsClient.DeleteAsync(new() {
			ConnectorId = $"{connectorName}"
		});
	}
}

