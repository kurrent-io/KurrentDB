// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public class CustomIndexesServiceTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	CustomIndexesService.CustomIndexesServiceClient Client => KurrentContext.CustomIndexesClient;

	readonly string _customIndexName = $"my-custom-index-{Guid.NewGuid()}";

	[Test]
	public async ValueTask can_create() {
		await Client.CreateCustomIndexAsync(new() {
			Name = _customIndexName,
			Filter = "e => e.type == 'my-event-type'",
			PartitionKeySelector = "e => e.number",
			PartitionKeyType = KeyType.Int32,
		});
	}

	[Test]
	[DependsOn(nameof(can_create))]
	public async ValueTask can_enable() {
	}

	[Test]
	[DependsOn(nameof(can_enable))]
	public async ValueTask can_disable() {
	}

	[Test]
	[DependsOn(nameof(can_disable))]
	public async ValueTask can_delete() {
		await Client.DeleteCustomIndexAsync(new() {
			Name = _customIndexName,
		});
	}

	[Test]
	public async ValueTask cannot_deletenon_existant() {
		await Client.DeleteCustomIndexAsync(new() { //qq this should throw.
			Name = "non_existant_index",
		});
	}
}
