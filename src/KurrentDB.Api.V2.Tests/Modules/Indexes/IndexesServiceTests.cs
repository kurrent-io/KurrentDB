// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesServiceTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient Client => KurrentContext.IndexesClient;

	static readonly string IndexName = $"my-user-index_{Guid.NewGuid()}";

	[Test]
	public async ValueTask can_create_started_by_default(CancellationToken ct) {
		var indexName = nameof(can_create_started_by_default) + Guid.NewGuid();
		await Client.CreateIndexAsync(
			new() {
				Name = indexName,
				Filter = "rec => rec.type == 'my-event-type'",
				Fields = {
					new Field() {
						Name = "number",
						Selector = "rec => rec.number",
						Type = FieldType.Int32,
					},
				},
			},
			cancellationToken: ct);
		await can_get(indexName, IndexState.Started, ct);
	}

	[Test]
	public async ValueTask can_create(CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = "rec => rec.type == 'my-event-type'",
				Fields = {
					new Field() {
						Name = "number",
						Selector = "rec => rec.number",
						Type = FieldType.Int32,
					},
				},
				Start = false,
			},
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Stopped, ct);

		// event type is mapped correctly
		var evt = await KurrentContext.StreamsClient.ReadAllForwardFiltered($"$UserIndex-{IndexName}", ct).FirstAsync();
		await Assert.That(evt.EventType).IsEqualTo("$IndexCreated");
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_create_idempotent(CancellationToken ct) {
		await Client.CreateIndexAsync(
			new() {
				Name = IndexName,
				Filter = "rec => rec.type == 'my-event-type'",
				Fields = {
					new Field() {
						Name = "number",
						Selector = "rec => rec.number",
						Type = FieldType.Int32,
					},
				},
				Start = false,
			},
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Stopped, ct);
	}

	[Test]
	[DependsOn(nameof(can_create_idempotent))]
	public async ValueTask cannot_create_different(CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateIndexAsync(
					new() {
						Name = IndexName,
						Filter = "rec => rec.type == 'my-OTHER-event-type'",
						Fields = {
							new Field() {
								Name = "number",
								Selector = "rec => rec.number",
								Type = FieldType.Int32,
							},
						},
					},
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Index '{IndexName}' already exists");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.AlreadyExists);
	}

	[Test]
	[DependsOn(nameof(cannot_create_different))]
	public async ValueTask can_start(CancellationToken ct) {
		await Client.StartIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Started, ct);
	}

	[Test]
	[DependsOn(nameof(can_start))]
	public async ValueTask can_start_idempotent(CancellationToken ct) {
		await Client.StartIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Started, ct);
	}

	[Test]
	[DependsOn(nameof(can_start_idempotent))]
	public async ValueTask can_stop(CancellationToken ct) {
		await Client.StopIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Stopped, ct);
	}

	[Test]
	[DependsOn(nameof(can_stop))]
	public async ValueTask can_stop_idempotent(CancellationToken ct) {
		await Client.StopIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);
		await can_get(IndexName, IndexState.Stopped, ct);
	}

	[Test]
	[DependsOn(nameof(can_stop_idempotent))]
	public async ValueTask can_list(CancellationToken ct) {
		var response = await Client.ListIndexesAsync(new(), cancellationToken: ct);

		await Assert.That(response!.Indexes.TryGetValue(IndexName, out var indexState)).IsTrue();
		await Assert.That(indexState!.Filter).IsEqualTo("rec => rec.type == 'my-event-type'");
		await Assert.That(indexState!.Fields.Count).IsEqualTo(1);
		await Assert.That(indexState!.Fields[0].Selector).IsEqualTo("rec => rec.number");
		await Assert.That(indexState!.Fields[0].Type).IsEqualTo(FieldType.Int32);
		await Assert.That(indexState!.State).IsEqualTo(IndexState.Stopped);
	}

	[Test]
	[DependsOn(nameof(can_list))]
	public async ValueTask can_delete(CancellationToken ct) {
		await Client.DeleteIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);

		await cannot_get("non-existant-index", ct);

		// no longer listed
		var response = await Client.ListIndexesAsync(new(), cancellationToken: ct);
		await Assert.That(response!.Indexes).DoesNotContainKey(IndexName);
	}

	[Test]
	[DependsOn(nameof(can_delete))]
	public async ValueTask can_delete_idempotent(CancellationToken ct) {
		await Client.DeleteIndexAsync(
			new() { Name = IndexName },
			cancellationToken: ct);
		await cannot_get("non-existant-index", ct);
	}

	[Test]
	[Arguments("UPPER_CASE_NOT_ALLOWED")]
	[Arguments("space not allowed")]
	[Arguments("不允許使用中文字符")]
	public async ValueTask cannot_create_with_invalid_name(string name, CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateIndexAsync(
					new() {
						Name = name,
						Filter = "rec => rec.type == 'my-event-type'",
						Fields = {
							new Field() {
								Name = "number",
								Selector = "rec => rec.number",
								Type = FieldType.Int32,
							},
						},
					},
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Name can contain only lowercase alphanumeric characters, underscores and dashes");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[Arguments("foo")]
	[Arguments("rec => rec.type ==> 'my-event-type'")]
	[Arguments("(rec, f) => rec.type == 'my-event-type'")]
	public async ValueTask cannot_create_with_invalid_filter(string filter, CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateIndexAsync(
					new() {
						Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
						Filter = filter,
						Fields = {
							new Field() {
								Name = "number",
								Selector = "rec => rec.number",
								Type = FieldType.Int32,
							},
						},
					},
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Filter must be empty or a valid JavaScript function with exactly one argument");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[Arguments("foo")]
	[Arguments("rec => rec.type ==> 'my-event-type'")]
	[Arguments("(rec, f) => rec.type == 'my-event-type'")]
	public async ValueTask cannot_create_with_invalid_key_selector(string keySelector, CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateIndexAsync(
					new() {
						Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
						Filter = "rec => rec.type == 'my-event-type'",
						Fields = {
							new Field() {
								Name = "the-field",
								Selector = keySelector,
								Type = FieldType.Int32,
							},
						},
					},
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Field selector must be empty or a valid JavaScript function with exactly one argument");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	public async ValueTask cannot_create_with_invalid_key_type(CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateIndexAsync(
					new() {
						Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
						Filter = "rec => rec.type == 'my-event-type'",
						Fields = {
							new Field() {
								Name = "number",
								Selector = "rec => rec.number",
								Type = FieldType.Unspecified,
							},
						},
					},
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Field type must not be unspecified");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	public async ValueTask cannot_start_non_existant(CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.StartIndexAsync(
					new() { Name = "non-existant-index" },
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_stop_non_existant(CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.StopIndexAsync(
					new() { Name = "non-existant-index" },
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_delete_non_existant(CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.DeleteIndexAsync(
					new() { Name = "non-existant-index" },
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_get_non_existant(CancellationToken ct) {
		await cannot_get("non-existant-index", ct);
	}

	async ValueTask can_get(string indexName, IndexState expectedState, CancellationToken ct) {
		var response = await Client.GetIndexAsync(
			new() { Name = indexName },
			cancellationToken: ct);
		await Assert.That(response.Index.Filter).IsEqualTo("rec => rec.type == 'my-event-type'");
		await Assert.That(response.Index.Fields.Count).IsEqualTo(1);
		await Assert.That(response.Index.Fields[0].Selector).IsEqualTo("rec => rec.number");
		await Assert.That(response.Index.Fields[0].Type).IsEqualTo(FieldType.Int32);
		await Assert.That(response.Index.State).IsEqualTo(expectedState);
	}

	async ValueTask cannot_get(string name, CancellationToken ct) {
		var ex = await Assert
			.That(async () => {
				await Client.GetIndexAsync(
					new() { Name = name },
					cancellationToken: ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
