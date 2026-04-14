// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.Surge.Testing;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[UsedImplicitly]
public class PersistentSubscriptionFixture : SecondaryIndexingEnabledFixture;

[Trait("Category", "Integration")]
public class PersistentSubscriptionTests(PersistentSubscriptionFixture fixture, ITestOutputHelper output)
	: ClusterVNodeTests<PersistentSubscriptionFixture>(fixture, output) {

	[Fact]
	public async Task CreateOnUnknownIndexFails() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var result = await Fixture.CreatePersistentSubscriptionToIndex(
			"$idx-nonexistent", "test-group", cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Fail,
			result.Result);
	}

	[Fact]
	public async Task CreateAndDeleteSucceeds() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"test-group-{Guid.NewGuid():N}";

		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			createResult.Result);

		var deleteResult = await Fixture.DeletePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, cts.Token);

		Assert.Equal(
			ClientMessage.DeletePersistentSubscriptionToIndexCompleted.DeletePersistentSubscriptionToIndexResult.Success,
			deleteResult.Result);
	}

	[Fact]
	public async Task CreateDuplicateFails() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"test-group-{Guid.NewGuid():N}";

		var firstResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			firstResult.Result);

		var secondResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.AlreadyExists,
			secondResult.Result);
	}

	[Fact]
	public async Task DeleteNonExistentFails() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var result = await Fixture.DeletePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, $"nonexistent-group-{Guid.NewGuid():N}", cts.Token);

		Assert.Equal(
			ClientMessage.DeletePersistentSubscriptionToIndexCompleted.DeletePersistentSubscriptionToIndexResult.DoesNotExist,
			result.Result);
	}
}
