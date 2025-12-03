// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesServiceTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	CustomIndexesService.CustomIndexesServiceClient Client => KurrentContext.CustomIndexesClient;

	static readonly string CustomIndexName = $"my-custom-index_{Guid.NewGuid()}";

	[Test]
	public async ValueTask can_create() {
		await Client.CreateCustomIndexAsync(new() {
			Name = CustomIndexName,
			Filter = "e => e.type == 'my-event-type'",
			PartitionKeySelector = "e => e.number",
			PartitionKeyType = KeyType.Int32,
		});
		await can_get(CustomIndexStatus.Disabled);
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_create_idempotent() {
		await Client.CreateCustomIndexAsync(new() {
			Name = CustomIndexName,
			Filter = "e => e.type == 'my-event-type'",
			PartitionKeySelector = "e => e.number",
			PartitionKeyType = KeyType.Int32,
		});
		await can_get(CustomIndexStatus.Disabled);
	}

	[Test]
	[DependsOn(nameof(can_create_idempotent))]
	public async ValueTask cannot_create_different() {
		var ex = await Assert
			.That(async () => {
				await Client.CreateCustomIndexAsync(new() {
					Name = CustomIndexName,
					Filter = "e => e.type == 'my-OTHER-event-type'",
					PartitionKeySelector = "e => e.number",
					PartitionKeyType = KeyType.Int32,
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo($"Custom Index '{CustomIndexName}' already exists");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.AlreadyExists);
	}

	[Test]
	[DependsOn(nameof(cannot_create_different))]
	public async ValueTask can_enable() {
		await Client.EnableCustomIndexAsync(new() {
			Name = CustomIndexName,
		});
		await can_get(CustomIndexStatus.Enabled);
	}

	[Test]
	[DependsOn(nameof(can_enable))]
	public async ValueTask can_enable_idempotent() {
		await Client.EnableCustomIndexAsync(new() {
			Name = CustomIndexName,
		});
		await can_get(CustomIndexStatus.Enabled);
	}

	[Test]
	[DependsOn(nameof(can_enable_idempotent))]
	public async ValueTask can_disable() {
		await Client.DisableCustomIndexAsync(new() {
			Name = CustomIndexName,
		});
		await can_get(CustomIndexStatus.Disabled);
	}

	[Test]
	[DependsOn(nameof(can_disable))]
	public async ValueTask can_disable_idempotent() {
		await Client.DisableCustomIndexAsync(new() {
			Name = CustomIndexName,
		});
		await can_get(CustomIndexStatus.Disabled);
	}

	[Test]
	[DependsOn(nameof(can_disable_idempotent))]
	public async ValueTask can_list() {
		var response = await Client.ListCustomIndexesAsync(new());

		await Assert.That(response!.CustomIndexes.TryGetValue(CustomIndexName, out var customIndexState)).IsTrue();
		await Assert.That(customIndexState!.Filter).IsEqualTo("e => e.type == 'my-event-type'");
		await Assert.That(customIndexState!.PartitionKeySelector).IsEqualTo("e => e.number");
		await Assert.That(customIndexState!.PartitionKeyType).IsEqualTo(KeyType.Int32);
		await Assert.That(customIndexState!.Status).IsEqualTo(CustomIndexStatus.Disabled);
	}

	[Test]
	[DependsOn(nameof(can_list))]
	public async ValueTask can_delete() {
		await Client.DeleteCustomIndexAsync(new() {
			Name = CustomIndexName,
		});

		await cannot_get("non-existant-index");

		// no longer listed
		var response = await Client.ListCustomIndexesAsync(new());
		await Assert.That(response!.CustomIndexes).DoesNotContainKey(CustomIndexName);
	}

	[Test]
	[DependsOn(nameof(can_delete))]
	public async ValueTask can_delete_idempotent() {
		await Client.DeleteCustomIndexAsync(new() {
			Name = CustomIndexName,
		});
		await cannot_get("non-existant-index");
	}

	[Test]
	[Arguments("UPPER_CASE_NOT_ALLOWED")]
	[Arguments("space not allowed")]
	[Arguments("不允許使用中文字符")]
	public async ValueTask cannot_create_with_invalid_name(string name) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateCustomIndexAsync(new() {
					Name = name,
					Filter = "e => e.type == 'my-event-type'",
					PartitionKeySelector = "e => e.number",
					PartitionKeyType = KeyType.Int32,
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Name can contain only lowercase alphanumeric characters, underscores and dashes");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[Arguments("foo")]
	[Arguments("e => e.type ==> 'my-event-type'")]
	[Arguments("(e, f) => e.type == 'my-event-type'")]
	public async ValueTask cannot_create_with_invalid_filter(string filter) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateCustomIndexAsync(new() {
					Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
					Filter = filter,
					PartitionKeySelector = "e => e.number",
					PartitionKeyType = KeyType.Int32,
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Filter must be a valid JavaScript function with exactly one argument");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[Arguments("foo")]
	[Arguments("e => e.type ==> 'my-event-type'")]
	[Arguments("(e, f) => e.type == 'my-event-type'")]
	public async ValueTask cannot_create_with_invalid_key_selector(string keySelector) {
		var ex = await Assert
			.That(async () => {
				await Client.CreateCustomIndexAsync(new() {
					Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
					Filter = "e => e.type == 'my-event-type'",
					PartitionKeySelector = keySelector,
					PartitionKeyType = KeyType.Int32,
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Partition key selector must be a valid JavaScript function with exactly one argument");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	//qq currently deosn't throw, what key type do we end up with, may be missing validation
	//[Test]
	//public async ValueTask cannot_create_with_invalid_key_type() {
	//	var ex = await Assert
	//		.That(async () => {
	//			await Client.CreateCustomIndexAsync(new() {
	//				Name = $"{nameof(cannot_create_with_invalid_filter)}-{Guid.NewGuid()}",
	//				Filter = "e => e.type == 'my-event-type'",
	//				PartitionKeySelector = "e => e.number",
	//				PartitionKeyType = KeyType.Unspecified,
	//			});
	//		})
	//		.Throws<RpcException>();

	//	await Assert.That(ex!.Status.Detail).IsEqualTo("Partition key selector must be a valid JavaScript function with exactly one argument");
	//	await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	//}

	[Test]
	public async ValueTask cannot_enable_non_existant() {
		var ex = await Assert
			.That(async () => {
				await Client.EnableCustomIndexAsync(new() {
					Name = "non-existant-index",
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Custom Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_disable_non_existant() {
		var ex = await Assert
			.That(async () => {
				await Client.DisableCustomIndexAsync(new() {
					Name = "non-existant-index",
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Custom Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_delete_non_existant() {
		var ex = await Assert
			.That(async () => {
				await Client.DeleteCustomIndexAsync(new() {
					Name = "non-existant-index",
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Custom Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask cannot_get_non_existant() {
		await cannot_get("non-existant-index");
	}

	async ValueTask can_get(CustomIndexStatus expectedStatus) {
		var response = await Client.GetCustomIndexAsync(new() { Name = CustomIndexName });
		await Assert.That(response.CustomIndex.Filter).IsEqualTo("e => e.type == 'my-event-type'");
		await Assert.That(response.CustomIndex.PartitionKeySelector).IsEqualTo("e => e.number");
		await Assert.That(response.CustomIndex.PartitionKeyType).IsEqualTo(KeyType.Int32);
		await Assert.That(response.CustomIndex.Status).IsEqualTo(expectedStatus);
	}

	async ValueTask cannot_get(string name) {
		var ex = await Assert
			.That(async () => {
				await Client.GetCustomIndexAsync(new() {
					Name = name,
				});
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.Detail).IsEqualTo("Custom Index 'non-existant-index' does not exist");
		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}
}
