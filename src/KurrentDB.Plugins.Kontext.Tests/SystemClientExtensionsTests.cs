// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Kontext;
using KurrentDB.Kontext.Mcp;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class SystemClientExtensionsTests {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	ISystemClient SystemClient => NodeShim.Node.Services.GetRequiredService<ISystemClient>();

	// -------- Read --------

	async Task<string> WriteTestEvents(int count) {
		var streamName = $"test-read-{Guid.NewGuid():N}";
		var events = new Event[count];
		for (var i = 0; i < count; i++) {
			var data = JsonSerializer.SerializeToUtf8Bytes(new { index = i, value = $"event-{i}" });
			events[i] = new Event(Guid.NewGuid(), "TestEvent", isJson: true, data);
		}
		await SystemClient.Writing.WriteEvents(streamName, events, requireLeader: false, principal: SystemAccounts.System);
		return streamName;
	}

	[Test]
	public async Task ReadAsync_Single_Event_Returns_One_Result() {
		var stream = await WriteTestEvents(3);
		var client = SystemClient;

		var results = new List<EventResult>();
		await foreach (var resolved in client.ReadAsync(stream, 0))
			results.Add(resolved.ToEventResult(stream));

		results.Count.ShouldBe(1);
		results[0].Stream.ShouldBe(stream);
		results[0].EventNumber.ShouldBe(0);
		results[0].EventType.ShouldBe("TestEvent");
	}

	[Test]
	public async Task ReadAsync_Single_Event_Parses_Json_Data() {
		var stream = await WriteTestEvents(1);
		var client = SystemClient;

		await foreach (var resolved in client.ReadAsync(stream, 0)) {
			var evt = resolved.ToEventResult(stream);
			evt.Data.IsEmpty.ShouldBeFalse();
			using var doc = JsonDocument.Parse(evt.Data);
			doc.RootElement.GetProperty("index").GetInt32().ShouldBe(0);
			doc.RootElement.GetProperty("value").GetString().ShouldBe("event-0");
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Range_Returns_Correct_Events() {
		var stream = await WriteTestEvents(5);
		var client = SystemClient;

		var results = new List<EventResult>();
		await foreach (var resolved in client.ReadAsync(stream, 1, 3))
			results.Add(resolved.ToEventResult(stream));

		results.Count.ShouldBe(3);
		results[0].EventNumber.ShouldBe(1);
		results[1].EventNumber.ShouldBe(2);
		results[2].EventNumber.ShouldBe(3);
	}

	[Test]
	public async Task ReadAsync_Populates_CommitPosition() {
		var stream = await WriteTestEvents(1);
		var client = SystemClient;

		await foreach (var resolved in client.ReadAsync(stream, 0)) {
			var evt = resolved.ToEventResult(stream);
			evt.CommitPosition.ShouldNotBeNull();
			evt.CommitPosition!.Value.ShouldBeGreaterThan(0);
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Populates_Timestamp() {
		var stream = await WriteTestEvents(1);
		var client = SystemClient;

		await foreach (var resolved in client.ReadAsync(stream, 0)) {
			var evt = resolved.ToEventResult(stream);
			evt.Timestamp.ShouldNotBeNull();
			evt.Timestamp!.Value.ShouldBeGreaterThan(DateTime.MinValue);
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Empty_Stream_Yields_Nothing() {
		var client = SystemClient;

		var count = 0;
		await foreach (var _ in client.ReadAsync($"nonexistent-{Guid.NewGuid():N}", 0))
			count++;

		count.ShouldBe(0);
	}

	[Test]
	public async Task ReadAsync_Strips_Data_From_Non_Json_Events() {
		var streamName = $"test-binary-{Guid.NewGuid():N}";

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "BinaryEvent", isJson: false, new byte[] { 0x01, 0x02, 0x03 }),
		], requireLeader: false, principal: SystemAccounts.System);

		var results = new List<EventResult>();
		await foreach (var resolved in SystemClient.ReadAsync(streamName, 0))
			results.Add(resolved.ToEventResult(streamName));

		results.Count.ShouldBe(1);
		results[0].EventType.ShouldBe("BinaryEvent");
		// The IsJson=false flag tells ReadAsync to strip the payload — non-JSON would
		// otherwise produce invalid JSON when serialized to the MCP/HTTP consumer.
		results[0].Data.IsEmpty.ShouldBeTrue();
	}

	// -------- Write --------

	[Test]
	public async Task WriteAsync_Writes_Event_To_Stream(CancellationToken ct) {
		var streamName = $"test-write-{Guid.NewGuid():N}";
		var client = SystemClient;

		var data = JsonSerializer.SerializeToUtf8Bytes(new { key = "value" });
		await client.WriteAsync(streamName, "TestEvent", data);

		var events = new List<KurrentDB.Core.Data.ResolvedEvent>();
		await foreach (var resolved in SystemClient.Reading.ReadStreamForwards(
			streamName, StreamRevision.Start, 10, ct))
			events.Add(resolved);

		events.Count.ShouldBe(1);
		var record = events[0].OriginalEvent;
		record.EventType.ShouldBe("TestEvent");
		record.IsJson.ShouldBeTrue();

		var written = JsonDocument.Parse(record.Data).RootElement;
		written.GetProperty("key").GetString().ShouldBe("value");
	}

	[Test]
	public async Task WriteBatchAsync_Routes_Events_To_Their_Streams(CancellationToken ct) {
		var streamA = $"test-batch-a-{Guid.NewGuid():N}";
		var streamB = $"test-batch-b-{Guid.NewGuid():N}";
		var client = SystemClient;

		var pending = new List<PendingEvent> {
			new(streamA, "Created", "{\"x\":1}"u8.ToArray()),
			new(streamB, "Created", "{\"y\":2}"u8.ToArray()),
			new(streamA, "Updated", "{\"x\":2}"u8.ToArray()),
		};
		await client.WriteBatchAsync(pending);

		var countA = 0;
		await foreach (var _ in SystemClient.Reading.ReadStreamForwards(streamA, StreamRevision.Start, 100, ct))
			countA++;
		var countB = 0;
		await foreach (var _ in SystemClient.Reading.ReadStreamForwards(streamB, StreamRevision.Start, 100, ct))
			countB++;

		countA.ShouldBe(2);
		countB.ShouldBe(1);
	}

	[Test]
	public async Task WriteBatchAsync_Empty_List_Is_NoOp(CancellationToken ct) {
		var client = SystemClient;
		await client.WriteBatchAsync([]);
		// No stream was touched and no exception was thrown.
	}

	// -------- Head position --------

	[Test]
	public async Task GetHeadPositionAsync_Returns_NonZero_When_Log_Has_Events(CancellationToken ct) {
		var streamName = $"test-head-{Guid.NewGuid():N}";
		var client = SystemClient;
		await client.WriteAsync(streamName, "TestEvent", "{}"u8.ToArray());

		var head = await client.GetHeadPositionAsync(ct);

		head.ShouldBeGreaterThan(0UL);
	}

	[Test]
	public async Task WriteAsync_Multiple_Events_Append_To_Same_Stream(CancellationToken ct) {
		var streamName = $"test-write-{Guid.NewGuid():N}";
		var client = SystemClient;

		await client.WriteAsync(streamName, "Event1", "{}"u8.ToArray());
		await client.WriteAsync(streamName, "Event2", "{}"u8.ToArray());
		await client.WriteAsync(streamName, "Event3", "{}"u8.ToArray());

		var count = 0;
		await foreach (var _ in SystemClient.Reading.ReadStreamForwards(
			streamName, StreamRevision.Start, 100, ct))
			count++;

		count.ShouldBe(3);
	}

	// -------- Subscribe --------

	[Test]
	public async Task SubscribeToAll_Receives_Written_Events(CancellationToken ct) {
		var streamName = $"test-sub-{Guid.NewGuid():N}";
		var client = SystemClient;

		var events = new[] {
			new Event(Guid.NewGuid(), "EventA", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { key = "value1" })),
			new Event(Guid.NewGuid(), "EventB", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { key = "value2" })),
		};
		await SystemClient.Writing.WriteEvents(streamName, events, requireLeader: false, principal: SystemAccounts.System);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(10));

		var received = new List<ResolvedEvent>();
		await foreach (var evt in client.SubscribeToAll(null, EventFilter.Unfiltered, cts.Token)) {
			if (evt.Event is null)
				continue;
			if (evt.Event.Value.OriginalStreamId == streamName)
				received.Add(evt.Event.Value);

			if (received.Count >= 2)
				break;
		}

		received.Count.ShouldBe(2);

		received[0].OriginalEvent.EventType.ShouldBe("EventA");
		received[0].OriginalEventNumber.ShouldBe(0L);
		received[0].OriginalStreamId.ShouldBe(streamName);
		received[0].OriginalPosition!.Value.CommitPosition.ShouldBeGreaterThan(0L);

		received[1].OriginalEvent.EventType.ShouldBe("EventB");
		received[1].OriginalEventNumber.ShouldBe(1L);
	}

	[Test]
	public async Task SubscribeToAll_Resumes_From_Checkpoint(CancellationToken ct) {
		var streamName = $"test-resume-{Guid.NewGuid():N}";
		var client = SystemClient;

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "First", isJson: true, "{}", "{}"),
		], requireLeader: false, principal: SystemAccounts.System);

		using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts1.CancelAfter(TimeSpan.FromSeconds(10));

		(ulong Commit, ulong Prepare)? checkpoint = null;
		await foreach (var evt in client.SubscribeToAll(null, EventFilter.Unfiltered, cts1.Token)) {
			if (evt.Event is null)
				continue;
			if (evt.Event.Value.OriginalStreamId == streamName) {
				var pos = evt.Event.Value.OriginalPosition!.Value;
				checkpoint = ((ulong)pos.CommitPosition, (ulong)pos.PreparePosition);
				break;
			}
		}

		checkpoint.ShouldNotBeNull();

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "Second", isJson: true, "{}", "{}"),
		], requireLeader: false, principal: SystemAccounts.System);

		using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts2.CancelAfter(TimeSpan.FromSeconds(10));

		var received = new List<ResolvedEvent>();
		await foreach (var evt in client.SubscribeToAll(checkpoint, EventFilter.Unfiltered, cts2.Token)) {
			if (evt.Event is null)
				continue;
			if (evt.Event.Value.OriginalStreamId == streamName)
				received.Add(evt.Event.Value);

			if (received.Count >= 1)
				break;
		}

		received.Count.ShouldBe(1);
		received[0].OriginalEvent.EventType.ShouldBe("Second");
	}

	[Test]
	public async Task SubscribeToAll_Event_Data_Is_Accessible(CancellationToken ct) {
		var streamName = $"test-data-{Guid.NewGuid():N}";
		var client = SystemClient;
		var payload = new { name = "test", count = 42 };

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "DataEvent", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(payload)),
		], requireLeader: false, principal: SystemAccounts.System);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(10));

		await foreach (var evt in client.SubscribeToAll(null, EventFilter.Unfiltered, cts.Token)) {
			if (evt.Event is null || evt.Event.Value.OriginalStreamId != streamName)
				continue;

			var data = evt.Event.Value.OriginalEvent.Data;
			data.Length.ShouldBeGreaterThan(0);
			var json = JsonDocument.Parse(data);
			json.RootElement.GetProperty("name").GetString().ShouldBe("test");
			json.RootElement.GetProperty("count").GetInt32().ShouldBe(42);
			break;
		}
	}
}