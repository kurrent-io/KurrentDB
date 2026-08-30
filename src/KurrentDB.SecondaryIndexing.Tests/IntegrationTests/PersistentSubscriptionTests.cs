// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.UserManagement;
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

	[Fact]
	public async Task LiveDeliveryContinuesAfterAcksAreProcessed() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"psub-ack-{Guid.NewGuid():N}";
		var stream = $"psub-ack-{Guid.NewGuid():N}";

		// Create subscription from the end so only new events arrive.
		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group,
			startFrom: new TFPos(-1, -1),
			ct: cts.Token);
		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			createResult.Result);

		// Connect with allowedInFlightMessages = 5 via the fixture helper, then
		// write 15 events. Without ack routing, the subscription stalls after 5
		// events because the acks never reach the index service and in-flight
		// messages are never released.
		const int totalEvents = 15;
		var eventNames = Enumerable.Range(1, totalEvents).Select(i => $"event-{i}").ToArray();

		var receiveTask = ConnectAndCollectFromStream(
			SystemStreams.DefaultSecondaryIndex, group, stream,
			expectedCount: totalEvents, cts.Token, allowedInFlightMessages: 5);

		await Task.Delay(500, cts.Token);
		await Fixture.AppendToStream(stream, eventNames);

		var (events, _) = await receiveTask;

		Assert.Equal(totalEvents, events.Count);
		var eventData = events
			.Select(e => Encoding.UTF8.GetString(e.Event.Data.Span))
			.ToList();
		Assert.Equal(eventNames, eventData);
	}

	[Fact]
	public async Task CatchUpDeliversAllEventsAcrossMultipleBatches() {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var group = $"psub-batch-{Guid.NewGuid():N}";
		var stream = $"psub-batch-{Guid.NewGuid():N}";

		// Write 6 events BEFORE creating the subscription (catch-up phase).
		// With readBatchSize=2, the subscription needs 3 read batches to deliver all events.
		// Before the fix, FetchIndexCompleted returned the request position instead of
		// the last event's position, causing the subscription to re-read the first batch
		// indefinitely and never advance.
		var eventNames = Enumerable.Range(1, 6).Select(i => $"event-{i}").ToArray();
		await Fixture.AppendToStream(stream, eventNames);
		await Fixture.ReadUntil(SystemStreams.DefaultSecondaryIndex, 6, forwards: true,
			timeout: TimeSpan.FromSeconds(10), ct: cts.Token);

		var createResult = await Fixture.CreatePersistentSubscriptionToIndex(
			SystemStreams.DefaultSecondaryIndex, group,
			readBatchSize: 2,
			ct: cts.Token);
		Assert.Equal(
			ClientMessage.CreatePersistentSubscriptionToIndexCompleted.CreatePersistentSubscriptionToIndexResult.Success,
			createResult.Result);

		// Connect and collect events, filtering for our stream. The index contains
		// events from all streams (including parallel tests), so we collect until
		// we have our 6 events. If position advancement is broken, the subscription
		// loops on the first batch and this times out.
		var (events, _) = await ConnectAndCollectFromStream(
			SystemStreams.DefaultSecondaryIndex, group, stream,
			expectedCount: 6, cts.Token);

		Assert.Equal(6, events.Count);

		var eventData = events
			.Select(e => Encoding.UTF8.GetString(e.Event.Data.Span))
			.ToList();
		Assert.Equal(eventNames, eventData);
	}

	/// <summary>
	/// Connects to a persistent subscription and collects events from the specified
	/// <paramref name="streamId"/>, acking all events (including those from other
	/// streams in the index). Returns when <paramref name="expectedCount"/> events
	/// from the target stream are collected, along with the correlation ID for
	/// unsubscribing.
	/// </summary>
	private async Task<(List<ResolvedEvent> Events, Guid CorrelationId)> ConnectAndCollectFromStream(
		string indexName, string group, string streamId,
		int expectedCount, CancellationToken ct, int allowedInFlightMessages = 10) {
		var corrId = Guid.NewGuid();
		var matched = new List<ResolvedEvent>();
		string? subscriptionId = null;
		var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var done = new TaskCompletionSource<List<ResolvedEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);

		var semaphore = new SemaphoreSlim(1, 1);
		var envelope = new ContinuationEnvelope(OnMessage, semaphore, ct);

		Fixture.Publisher.Publish(new ClientMessage.ConnectToPersistentSubscriptionToIndex(
			internalCorrId: corrId,
			correlationId: corrId,
			envelope: envelope,
			connectionId: corrId,
			connectionName: $"test-connection-{corrId:N}",
			groupName: group,
			indexName: indexName,
			allowedInFlightMessages: allowedInFlightMessages,
			from: "",
			user: SystemAccounts.System));

		await confirmed.Task.WaitAsync(ct);
		var events = await done.Task.WaitAsync(ct);
		return (events, corrId);

		Task OnMessage(Message msg, CancellationToken token) {
			switch (msg) {
				case ClientMessage.PersistentSubscriptionConfirmation confirmation:
					subscriptionId = confirmation.SubscriptionId;
					confirmed.TrySetResult();
					break;
				case ClientMessage.PersistentSubscriptionStreamEventAppeared appeared:
					Fixture.Publisher.Publish(new ClientMessage.PersistentSubscriptionAckEvents(
						corrId, corrId, new NoopEnvelope(),
						subscriptionId,
						[appeared.Event.OriginalEvent.EventId],
						SystemAccounts.System));

					if (appeared.Event.Event.EventStreamId == streamId) {
						matched.Add(appeared.Event);
						if (matched.Count >= expectedCount)
							done.TrySetResult(matched.ToList());
					}
					break;
				case ClientMessage.SubscriptionDropped dropped:
					done.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					confirmed.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					break;
			}
			return Task.CompletedTask;
		}
	}
}
