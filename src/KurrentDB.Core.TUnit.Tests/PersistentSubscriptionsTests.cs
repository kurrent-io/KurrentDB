// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client.PersistentSubscriptions;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing;

namespace KurrentDB.Core.TUnit.Tests;

public class PersistentSubscriptionsTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	PersistentSubscriptions.PersistentSubscriptionsClient PsClient => KurrentContext.PersistentSubscriptionsClient;
	StreamsService.StreamsServiceClient StreamsClient => KurrentContext.StreamsV2Client;

	// Create a PS and subscribe to it, don't ack or nack anything, subscription should continue (and park the messages)
	// Previously the serverside buffer would eventually empty and not be refilled without acks/naks and the subscription would stop.
	//   A subsystem restart or leader change was necessary to continue the subscription.
	[Test]
	[Timeout(30_000)]
	public async Task subscription_continues_to_park_without_acks_or_nacks(CancellationToken ct) {
		var cid = Guid.NewGuid();
		var testName = nameof(subscription_continues_to_park_without_acks_or_nacks);
		var stream = $"{testName}-stream-{cid}";
		var group = $"{testName}-group-{cid}";
		var eventType = $"{testName}-event-{cid}";
		var data = ByteString.CopyFromUtf8("{}");
		var expectedEvents = 100;

		// write 100 events
		for (var i = 0; i < expectedEvents; i++) {
			await StreamsClient.AppendAsync(
				new() {
					ExpectedRevision = (long)ExpectedRevisionConstants.Any,
					Stream = stream,
					Records = {
						new AppendRecord() {
							RecordId = Guid.NewGuid().ToString(),
							Schema = new() {
								Name = eventType,
								Format = SchemaFormat.Json,
							},
							Data = data,
						}
					},
				},
				cancellationToken: ct);
		}

		// create PS with serverside catchup buffer of 50 (HistoryBufferSize) and no retries
		await PsClient.CreateAsync(
			new CreateReq {
				Options = new() {
					GroupName = group,
					Stream = new() {
						StreamIdentifier = new() {
							StreamName = ByteString.CopyFromUtf8(stream),
						},
						Start = new(),
					},
					Settings = new() {
						MaxRetryCount = 0,
						MessageTimeoutMs = 50,
						ReadBatchSize = 20,
						HistoryBufferSize = 50,
					},
				}
			},
			cancellationToken: ct);

		// subscribe to PS
		using var sub = PsClient.Read(cancellationToken: ct);

		await sub.RequestStream.WriteAsync(
			new() {
				Options = new() {
					GroupName = group,
					StreamIdentifier = new() {
						StreamName = ByteString.CopyFromUtf8(stream),
					},
					BufferSize = 10,
					UuidOption = new() {
						Structured = new()
					},
				}
			},
			cancellationToken: ct);


		// expect to receive all the events once (because no retries) even though we don't send acks/naks
		var receivedEvents = 0;
		try {
			await foreach (var response in sub.ResponseStream.ReadAllAsync(ct)) {
				if (response.Event is not null && ++receivedEvents == expectedEvents)
					return;
			}
		} catch {
			if (ct.IsCancellationRequested)
				Assert.Fail($"Received {receivedEvents} out of {expectedEvents}.");
		}

		Assert.Fail("ResponseStream isn't expected to complete");
	}
}
