// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.Testing.TUnit;
using RestSharp;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesServiceTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	CustomIndexesService.CustomIndexesServiceClient Client => KurrentContext.CustomIndexesClient;

	[Test]
	public async ValueTask create() {
		await Client.CreateCustomIndexAsync(new() {
			Name = "my-index",
			Filter = "e => e.type == 'my-event-type'",
			PartitionKeySelector = "e => e.number",
			PartitionKeyType = KeyType.Int32,
		});
	}

	[Test]
	[DependsOn(nameof(create))]
	public async ValueTask delete() {
		await Client.DeleteCustomIndexAsync(new() {
			Name = "my-index",
		});
	}
}

public class CustomIndexesServiceHttpTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IRestClient Client => KurrentContext.RestClientShim.Client;

	//qq in error cases we don't want 500
	[Test]
	public async ValueTask foo() {
		var response = await Client.PostAsync<Response>(
			new RestRequest("/v2/custom-indexes/create")
			.AddJsonBody(
				new {
					Name = "my-index-via-http",
					Filter = "e => e.type == 'my-event-type'",
					PartitionkeySelector = "e => e.number",
					PartitionkeyType = KeyType.Int32,
				},
			"application/json"), //qq correct content type?
			TestContext.CancellationToken);

		await Assert.That(response).IsNotNull();
	}

	record Response {
		[JsonPropertyName("dbVersion")] public string DbVersion { get; init; } = ""; //qq
	}

}
