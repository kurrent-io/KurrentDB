// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
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
			"$idx-nonexistent", "test-group", ct: cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Fail,
			result.Result);
	}

	[Fact]
	public async Task CreateAndDeleteSucceeds() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"test-group-{Guid.NewGuid():N}";

		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, ct: cts.Token);

		Assert.True(
			createResult.Result == ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			$"Create failed: {createResult.Result} — {createResult.Reason}");

		var deleteResult = await Fixture.DeletePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, ct: cts.Token);

		Assert.Equal(
			ClientMessage.DeletePersistentSubscriptionToIndexCompleted.DeletePersistentSubscriptionToIndexResult.Success,
			deleteResult.Result);
	}

	[Fact]
	public async Task CreateDuplicateFails() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"test-group-{Guid.NewGuid():N}";

		var firstResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, ct: cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			firstResult.Result);

		var secondResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, ct: cts.Token);

		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.AlreadyExists,
			secondResult.Result);
	}

	[Fact]
	public async Task ReceivesEventsFromDefaultIndex() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"psub-recv-{Guid.NewGuid():N}";

		// Write events BEFORE creating the subscription (catch-up phase)
		var stream = $"psub-test-{Guid.NewGuid():N}";
		await Fixture.AppendToStream(stream, "event-1", "event-2", "event-3");

		// Wait for events to be indexed
		await Fixture.ReadUntil(SystemStreams.DefaultSecondaryIndex, 3, forwards: true, timeout: TimeSpan.FromSeconds(10), ct: cts.Token);

		// Create subscription from the beginning
		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, ct: cts.Token);
		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			createResult.Result);

		// Connect and receive events
		var events = await Fixture.ConnectToPersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, 3, cts.Token);

		Assert.Equal(3, events.Count);
		// Verify the events come from our stream
		Assert.All(events, e => Assert.Equal(stream, e.Event.EventStreamId));
	}

	[Fact]
	public async Task ReceivesLiveEventsAfterCatchUp() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"psub-live-{Guid.NewGuid():N}";

		// Create subscription starting from the end so we only get new events
		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, startFrom: new TFPos(-1, -1), ct: cts.Token);
		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			createResult.Result);

		// Connect first, then write events
		var stream = $"psub-live-{Guid.NewGuid():N}";

		// Start collecting events in the background
		var receiveTask = Fixture.ConnectToPersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group, 2, cts.Token);

		// Give the connection time to be established
		await Task.Delay(500, cts.Token);

		// Write events — they should flow through the index to the persistent subscription live
		await Fixture.AppendToStream(stream, "live-1", "live-2");

		var events = await receiveTask;

		Assert.Equal(2, events.Count);
		Assert.All(events, e => Assert.Equal(stream, e.Event.EventStreamId));
	}

}
