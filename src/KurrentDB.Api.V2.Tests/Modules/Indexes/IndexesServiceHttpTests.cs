// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Testing.TUnit;
using RestSharp;

namespace KurrentDB.Api.Tests.Modules.Indexes;

// The HTTP Api is automatically generated from the gRPC, we just sanity check here.
public class IndexesServiceHttpTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IRestClient Client => KurrentContext.RestClientShim.Client;

	static readonly string IndexName = $"my-user-index-{Guid.NewGuid()}";

	[Test]
	public async ValueTask can_create(CancellationToken ct) {
		var request = new RestRequest($"/v2/indexes/{IndexName}")
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
		await can_get(expectedState: "INDEX_STATE_STOPPED", ct);
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_start(CancellationToken ct) {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/indexes/{IndexName}/start"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedState: "INDEX_STATE_STARTED", ct);
	}

	[Test]
	[DependsOn(nameof(can_start))]
	public async ValueTask can_stop(CancellationToken ct) {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/indexes/{IndexName}/stop"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await can_get(expectedState: "INDEX_STATE_STOPPED", ct);
	}

	[Test]
	[DependsOn(nameof(can_stop))]
	public async ValueTask can_list(CancellationToken ct) {
		var response = await Client.GetAsync<ListResponse>(
			new RestRequest($"/v2/indexes/"),
			ct);

		await Assert.That(response!.Indexes.TryGetValue(IndexName, out var indexState)).IsTrue();
		await Assert.That(indexState!.Filter).IsEqualTo("rec => rec.type == 'my-event-type'");
		await Assert.That(indexState!.Fields.Length).IsEqualTo(1);
		await Assert.That(indexState!.Fields[0].Selector).IsEqualTo("rec => rec.number");
		await Assert.That(indexState!.Fields[0].Type).IsEqualTo("FIELD_TYPE_INT_32");
		await Assert.That(indexState!.State).IsEqualTo("INDEX_STATE_STOPPED");
	}

	class ListResponse {
		public Dictionary<string, IndexState> Indexes { get; set; } = [];

		public class IndexState {
			public string Filter { get; set; } = "";
			public Field[] Fields { get; set; } = [];
			public string State { get; set; } = "";
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
			new RestRequest($"/v2/indexes/{IndexName}"),
			ct);

		await Assert.That(response.Content).IsJson("{}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// get -> 404
		var getResponse = await Client.GetAsync(
			new RestRequest($"/v2/indexes/{IndexName}"),
			ct);

		await Assert.That(getResponse.Content).IsJson($$"""
			{
				"code": 5,
				"message": "Index '{{IndexName}}' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "INDEX_NOT_FOUND",
						"domain": "indexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.indexes.errors.IndexNotFoundErrorDetails",
						"name": "{{IndexName}}"
					}
				]
			}
			""");
		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		// no longer listed
		var listResponse = await Client.GetAsync<ListResponse>(
			new RestRequest($"/v2/indexes/"),
			ct);

		await Assert.That(listResponse!.Indexes).DoesNotContainKey(IndexName);
	}

	async ValueTask can_get(string expectedState, CancellationToken ct) {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/indexes/{IndexName}"),
			ct);

		await Assert.That(response.Content).IsJson($$"""
			{
				"index": {
					"filter": "rec => rec.type == 'my-event-type'",
					"fields": [{
						"name": "number",
						"selector": "rec => rec.number",
						"type": "FIELD_TYPE_INT_32"
					}],
					"state": "{{expectedState}}"
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
				"message": "Index 'non-existant-index' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "INDEX_NOT_FOUND",
						"domain": "indexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.indexes.errors.IndexNotFoundErrorDetails",
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
				"message": "Index 'non-existant-index' does not exist",
				"details": [
					{
						"@type": "type.googleapis.com/google.rpc.ErrorInfo",
						"reason": "INDEX_NOT_FOUND",
						"domain": "indexes",
						"metadata": {}
					},
					{
						"@type": "type.googleapis.com/kurrentdb.protocol.v2.indexes.errors.IndexNotFoundErrorDetails",
						"name": "non-existant-index"
					}
				]
			}
			""");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
