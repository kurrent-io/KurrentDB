// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.Testing;
using KurrentDB.Testing.TUnit;
using RestSharp;

namespace KurrentDB.Ammeter;

public class VersionTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IRestClient Client => KurrentContext.RestClientShim.Client;

	[Test]
	[RequiresConfiguration("ExpectedVersion")]
	public async Task can_get_the_version() {
		var response = await Client.GetAsync<InfoResponse>(
			new RestRequest($"info"),
			TestContext.CancellationToken);

		var expectedVersion = ToolkitTestEnvironment.Configuration["ExpectedVersion"]!;

		Console.WriteLine($"Expected version {expectedVersion}");
		Console.WriteLine($"Found version {response!.DbVersion}");

		await Assert.That(response!.DbVersion).StartsWith(expectedVersion);
	}

	record InfoResponse {
		[JsonPropertyName("dbVersion")] public string DbVersion { get; init; } = "";
	}
}
