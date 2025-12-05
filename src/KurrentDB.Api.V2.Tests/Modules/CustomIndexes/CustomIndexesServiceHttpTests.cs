// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Testing.TUnit;
using RestSharp;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

// The HTTP Api is automatically generated from the gRPC, we just sanity check here.
public class CustomIndexesServiceHttpTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IRestClient Client => KurrentContext.RestClientShim.Client;

	static readonly string CustomIndexName = $"my-custom-index-{Guid.NewGuid()}";

	[Test]
	public async ValueTask can_create() {
		var request = new RestRequest($"/v2/custom-indexes/{CustomIndexName}")
			.AddJsonBody("""
				{
					"Filter": "e => e.type == 'my-event-type'",
					"PartitionKeySelector": "e => e.number",
					"PartitionKeyType": "KEY_TYPE_INT_32",
					"Enable": false
				}
				""");

		var response = await Client.ExecutePostAsync(request, TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_DISABLED");
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_enable() {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}/enable"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_ENABLED");
	}

	[Test]
	[DependsOn(nameof(can_enable))]
	public async ValueTask can_disable() {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}/disable"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_DISABLED");
	}

	[Test]
	[DependsOn(nameof(can_disable))]
	public async ValueTask can_list() {
		var response = await Client.GetAsync<ListResponse>(
			new RestRequest($"/v2/custom-indexes/"),
			TestContext.CancellationToken);

		await Assert.That(response!.CustomIndexes.TryGetValue(CustomIndexName, out var customIndexState)).IsTrue();
		await Assert.That(customIndexState!.Filter).IsEqualTo("e => e.type == 'my-event-type'");
		await Assert.That(customIndexState!.PartitionKeySelector).IsEqualTo("e => e.number");
		await Assert.That(customIndexState!.PartitionKeyType).IsEqualTo("KEY_TYPE_INT_32");
		await Assert.That(customIndexState!.Status).IsEqualTo("CUSTOM_INDEX_STATUS_DISABLED");
	}

	class ListResponse {
		public Dictionary<string, CustomIndexState> CustomIndexes { get; set; } = [];

		public class CustomIndexState {
			public string Filter { get; set; } = "";
			public string PartitionKeySelector { get; set; } = "";
			public string PartitionKeyType { get; set; } = "";
			public string Status { get; set; } = "";
		}
	}

	[Test]
	[DependsOn(nameof(can_list))]
	public async ValueTask can_delete() {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// get -> 404
		var getResponse = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(getResponse.Content).IsJson($$"""
			{
				"code": 5,
				"message": "Custom Index '{{CustomIndexName}}' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "CUSTOM_INDEX_NOT_FOUND",
						"domain": "customindexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.custom_indexes.errors.CustomIndexNotFoundErrorDetails",
						"name": "{{CustomIndexName}}"
					}
				]
			}
			""");
		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		// no longer listed
		var listResponse = await Client.GetAsync<ListResponse>(
			new RestRequest($"/v2/custom-indexes/"),
			TestContext.CancellationToken);

		await Assert.That(listResponse!.CustomIndexes).DoesNotContainKey(CustomIndexName);
	}

	async ValueTask can_get(string expectedStatus) {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson($$"""
			{
				"customIndex": {
					"filter": "e => e.type == 'my-event-type'",
					"partitionKeySelector": "e => e.number",
					"partitionKeyType": "KEY_TYPE_INT_32",
					"status": "{{expectedStatus}}"
				}
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async ValueTask cannot_create_with_invalid_name() {
		var illegalName = "UPPER CASE NOT ALLOWED";
		var request = new RestRequest($"/v2/custom-indexes/{illegalName}")
			.AddJsonBody("""
				{
					"Filter": "e => e.type == 'my-event-type'",
					"PartitionKeySelector": "e => e.number",
					"PartitionKeyType": "KEY_TYPE_INT_32"
				}
				""");

		var response = await Client.ExecutePostAsync(request, TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("""
			{
				"code": 3,
				"message": "Name can contain only lowercase alphanumeric characters, underscores and dashes",
				"details": []
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async ValueTask cannot_delete_non_existant() {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/custom-indexes/non-existant-index"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("""
			{
				"code": 5,
				"message": "Custom Index 'non-existant-index' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "CUSTOM_INDEX_NOT_FOUND",
						"domain": "customindexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.custom_indexes.errors.CustomIndexNotFoundErrorDetails",
						"name": "non-existant-index"
					}
				]
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_get_non_existant() {
		var response = await Client.ExecuteGetAsync(
			new RestRequest($"/v2/custom-indexes/non-existant-index"),
			TestContext.CancellationToken);

		await Assert.That(response.Content).IsJson("""
			{
				"code": 5,
				"message": "Custom Index 'non-existant-index' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "CUSTOM_INDEX_NOT_FOUND",
						"domain": "customindexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.custom_indexes.errors.CustomIndexNotFoundErrorDetails",
						"name": "non-existant-index"
					}
				]
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
