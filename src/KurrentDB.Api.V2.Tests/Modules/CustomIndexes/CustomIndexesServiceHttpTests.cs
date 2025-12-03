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
					"PartitionKeyType": "KEY_TYPE_INT_32"
				}
				""");

		var response = await Client.PostAsync(request, TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson("{}");
		await can_get(status: "STATUS_DISABLED");
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_enable() {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}/enable"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson("{}");
		await can_get(status: "STATUS_ENABLED");
	}

	[Test]
	[DependsOn(nameof(can_enable))]
	public async ValueTask can_disable() {
		var response = await Client.PostAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}/disable"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson("{}");
		await can_get(status: "STATUS_DISABLED");
	}

	[Test]
	[DependsOn(nameof(can_disable))]
	public async ValueTask can_list() {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson($$"""
			{
				"customIndexes": {
					"{{CustomIndexName}}": {
						"filter": "e => e.type == 'my-event-type'",
						"partitionKeySelector": "e => e.number",
						"partitionKeyType": "KEY_TYPE_INT_32",
						"status": "STATUS_DISABLED"
					}
				}
			}
			""");
	}

	[Test]
	[DependsOn(nameof(can_list))]
	public async ValueTask can_delete() {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson("{}");

		// get -> 404
		var getResponse = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(getResponse.Content).IsJson("""
			{
				"code":5,
				"message":"Custom Index has been deleted",
				"details":[]
			}
			""");

		// no longer listed
		var listResponse = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/"),
			TestContext.CancellationToken);

		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(listResponse.Content).IsJson("""
			{
				"customIndexes": {
				}
			}
			""");
	}

	async ValueTask can_get(string status) {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/{CustomIndexName}"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content).IsJson($$"""
			{
				"customIndex": {
					"filter": "e => e.type == 'my-event-type'",
					"partitionKeySelector": "e => e.number",
					"partitionKeyType": "KEY_TYPE_INT_32",
					"status": "{{status}}"
				}
			}
			""");
	}

	//qq
	//[Test]
	//public async ValueTask cannot_create_with_illegal_name() {
	//	var illegalName = "UPPER CASE NOT ALLOWED";
	//	var request = new RestRequest($"/v2/custom-indexes/{illegalName}")
	//		//qq probably don't need the body?
	//		.AddJsonBody("""
	//			{
	//				"Filter": "e => e.type == 'my-event-type'",
	//				"PartitionKeySelector": "e => e.number",
	//				"PartitionKeyType": "KEY_TYPE_INT_32"
	//			}
	//			""");

	//	var response = await Client.PostAsync(request, TestContext.CancellationToken);

	//	await Assert.That(response.Content).IsJson("{}");
	//}

	[Test]
	public async ValueTask cannot_delete_non_existant() {
		var response = await Client.DeleteAsync(
			new RestRequest($"/v2/custom-indexes/non-existant-index"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(response.Content).IsJson("""
			{
				"code":5,
				"message":"Custom Index does not exist",
				"details":[]
			}
			""");
	}

	[Test]
	public async ValueTask cannot_get_non_existant() {
		var response = await Client.GetAsync(
			new RestRequest($"/v2/custom-indexes/non-existant-index"),
			TestContext.CancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(response.Content).IsJson("""
			{
				"code":5,
				"message":"Custom Index does not exist",
				"details":[]
			}
			""");
	}
}
