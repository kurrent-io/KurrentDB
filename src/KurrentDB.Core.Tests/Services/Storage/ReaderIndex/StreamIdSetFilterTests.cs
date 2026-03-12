// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.ReaderIndex;

[TestFixture]
public class StreamIdSetFilterTests {
	private static EventRecord CreateEventRecord(string streamId, string eventType = "TestEvent") {
		return new EventRecord(
			eventNumber: 0,
			logPosition: 0,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: 0,
			transactionOffset: 0,
			eventStreamId: streamId,
			expectedVersion: -1,
			timeStamp: DateTime.UtcNow,
			flags: PrepareFlags.SingleWrite,
			eventType: eventType,
			data: Array.Empty<byte>(),
			metadata: null);
	}

	[Test]
	public void allows_event_from_stream_in_set() {
		var filter = EventFilter.StreamName.Set(false, "stream-a", "stream-b");
		var record = CreateEventRecord("stream-a");
		Assert.That(filter.IsEventAllowed(record), Is.True);
	}

	[Test]
	public void allows_event_from_another_stream_in_set() {
		var filter = EventFilter.StreamName.Set(false, "stream-a", "stream-b");
		var record = CreateEventRecord("stream-b");
		Assert.That(filter.IsEventAllowed(record), Is.True);
	}

	[Test]
	public void rejects_event_from_stream_not_in_set() {
		var filter = EventFilter.StreamName.Set(false, "stream-a", "stream-b");
		var record = CreateEventRecord("stream-c");
		Assert.That(filter.IsEventAllowed(record), Is.False);
	}

	[Test]
	public void rejects_event_with_partial_stream_name_match() {
		var filter = EventFilter.StreamName.Set(false, "stream");
		var record = CreateEventRecord("stream-a");
		Assert.That(filter.IsEventAllowed(record), Is.False);
	}

	[Test]
	public void when_is_all_stream_rejects_epoch_information_system_stream() {
		var filter = EventFilter.StreamName.Set(true, "$epoch-information", "stream-a");
		var record = CreateEventRecord("$epoch-information");
		Assert.That(filter.IsEventAllowed(record), Is.False);
	}

	[Test]
	public void when_is_all_stream_allows_non_system_stream_in_set() {
		var filter = EventFilter.StreamName.Set(true, "stream-a", "stream-b");
		var record = CreateEventRecord("stream-a");
		Assert.That(filter.IsEventAllowed(record), Is.True);
	}

	[Test]
	public void when_not_all_stream_allows_system_stream_in_set() {
		var filter = EventFilter.StreamName.Set(false, "$epoch-information");
		var record = CreateEventRecord("$epoch-information");
		Assert.That(filter.IsEventAllowed(record), Is.True);
	}

	[Test]
	public void parse_to_dto_returns_correct_values() {
		var filter = EventFilter.StreamName.Set(true, "stream-a", "stream-b");
		var dto = EventFilter.ParseToDto(filter);

		Assert.That(dto, Is.Not.Null);
		Assert.That(dto.Context, Is.EqualTo(EventFilter.StreamIdContext));
		Assert.That(dto.Type, Is.EqualTo("set"));
		Assert.That(dto.IsAllStream, Is.True);
		// The data contains the stream names comma-separated (order may vary due to HashSet)
		Assert.That(dto.Data, Does.Contain("stream-a"));
		Assert.That(dto.Data, Does.Contain("stream-b"));
	}
}
