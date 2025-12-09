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
	public async ValueTask can_create(CancellationToken ct) {
		var request = new RestRequest($"/v2/indexes/{CustomIndexName}")
			.AddJsonBody("""
				{
					"Filter": "rec => rec.type == 'my-event-type'",
					"Fields": [{
						"Name": "number",
						"Selector": "rec => rec.number",
						"Type": "FIELD_TYPE_INT_32"
					}],
					"Start": false
				}
				""");

		var response = await Client.ExecutePostAsync(request, ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_STOPPED", ct);
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_start(CancellationToken ct) {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/indexes/{CustomIndexName}/start"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_STARTED", ct);
	}

	[Test]
	[DependsOn(nameof(can_start))]
	public async ValueTask can_stop(CancellationToken ct) {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/indexes/{CustomIndexName}/stop"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedStatus: "CUSTOM_INDEX_STATUS_STOPPED", ct);
	}

	[Test]
	[DependsOn(nameof(can_stop))]
	public async ValueTask can_list(CancellationToken ct) {
		var response = await Client.GetAsync<ListResponse>(
			new RestRequest($"/v2/indexes/"),
			ct);

		await Assert.That(response!.CustomIndexes.TryGetValue(CustomIndexName, out var customIndexState)).IsTrue();
		await Assert.That(customIndexState!.Filter).IsEqualTo("rec => rec.type == 'my-event-type'");
		await Assert.That(customIndexState!.Fields.Length).IsEqualTo(1);
		await Assert.That(customIndexState!.Fields[0].Selector).IsEqualTo("rec => rec.number");
		await Assert.That(customIndexState!.Fields[0].Type).IsEqualTo("FIELD_TYPE_INT_32");
		await Assert.That(customIndexState!.Status).IsEqualTo("CUSTOM_INDEX_STATUS_STOPPED");
	}

	class ListResponse {
		public Dictionary<string, CustomIndexState> CustomIndexes { get; set; } = [];

		public class CustomIndexState {
			public string Filter { get; set; } = "";
			public Field[] Fields { get; set; } = [];
			public string Status { get; set; } = "";
		}

		public class Field {
			public string Name { get; set; } = "";
			public string Selector { get; set; } = "";
			public string Type { get; set; } = "";
		}
	}

	[Test]
	[DependsOn(nameof(can_list))]
	public async ValueTask can_delete(CancellationToken ct) {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/indexes/{CustomIndexName}"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// get -> 404
		var getResponse = await Client.GetAsync(
			new RestRequest($"/v2/indexes/{CustomIndexName}"),
			ct);

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
			new RestRequest($"/v2/indexes/"),
			ct);

		await Assert.That(listResponse!.CustomIndexes).DoesNotContainKey(CustomIndexName);
	}

	async ValueTask can_get(string expectedStatus, CancellationToken ct) {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/indexes/{CustomIndexName}"),
			ct);

		await Assert.That(response.Content).IsJson($$"""
			{
				"customIndex": {
					"filter": "rec => rec.type == 'my-event-type'",
					"fields": [{
						"name": "number",
						"selector": "rec => rec.number",
						"type": "FIELD_TYPE_INT_32"
					}],
					"status": "{{expectedStatus}}"
				}
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async ValueTask cannot_create_with_invalid_name(CancellationToken ct) {
		var illegalName = "UPPER CASE NOT ALLOWED";
		var request = new RestRequest($"/v2/indexes/{illegalName}")
			.AddJsonBody("""
				{
					"Filter": "rec => rec.type == 'my-event-type'",
					"Fields": [{
						"Name": "number",
						"Selector": "rec => rec.number",
						"Type": "FIELD_TYPE_INT_32"
					}]
				}
				""");

		var response = await Client.ExecutePostAsync(request, ct);

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
	public async ValueTask cannot_delete_non_existant(CancellationToken ct) {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/indexes/non-existant-index"),
			ct);

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
	public async ValueTask cannot_get_non_existant(CancellationToken ct) {
		var response = await Client.ExecuteGetAsync(
			new RestRequest($"/v2/indexes/non-existant-index"),
			ct);

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
