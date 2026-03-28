using System.Text.Json;
using Kurrent.Kontext;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class KontextClientTests {
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
		await SystemClient.Writing.WriteEvents(streamName, events);
		return streamName;
	}

	[Test]
	public async Task ReadAsync_Single_Event_Returns_One_Result() {
		var stream = await WriteTestEvents(3);
		var client = new KontextClient(SystemClient);

		var results = new List<EventResult>();
		await foreach (var evt in client.ReadAsync(stream, 0))
			results.Add(evt);

		results.Count.ShouldBe(1);
		results[0].Stream.ShouldBe(stream);
		results[0].EventNumber.ShouldBe(0);
		results[0].EventType.ShouldBe("TestEvent");
	}

	[Test]
	public async Task ReadAsync_Single_Event_Parses_Json_Data() {
		var stream = await WriteTestEvents(1);
		var client = new KontextClient(SystemClient);

		await foreach (var evt in client.ReadAsync(stream, 0)) {
			evt.Data.ShouldNotBeNull();
			evt.Data!.Value.GetProperty("index").GetInt32().ShouldBe(0);
			evt.Data!.Value.GetProperty("value").GetString().ShouldBe("event-0");
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Range_Returns_Correct_Events() {
		var stream = await WriteTestEvents(5);
		var client = new KontextClient(SystemClient);

		var results = new List<EventResult>();
		await foreach (var evt in client.ReadAsync(stream, 1, 3))
			results.Add(evt);

		results.Count.ShouldBe(3);
		results[0].EventNumber.ShouldBe(1);
		results[1].EventNumber.ShouldBe(2);
		results[2].EventNumber.ShouldBe(3);
	}

	[Test]
	public async Task ReadAsync_Populates_CommitPosition() {
		var stream = await WriteTestEvents(1);
		var client = new KontextClient(SystemClient);

		await foreach (var evt in client.ReadAsync(stream, 0)) {
			evt.CommitPosition.ShouldNotBeNull();
			evt.CommitPosition!.Value.ShouldBeGreaterThan(0);
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Populates_Timestamp() {
		var stream = await WriteTestEvents(1);
		var client = new KontextClient(SystemClient);

		await foreach (var evt in client.ReadAsync(stream, 0)) {
			evt.Timestamp.ShouldNotBeNull();
			evt.Timestamp!.Value.ShouldBeGreaterThan(DateTime.MinValue);
			break;
		}
	}

	[Test]
	public async Task ReadAsync_Empty_Stream_Yields_Nothing() {
		var client = new KontextClient(SystemClient);

		var count = 0;
		await foreach (var _ in client.ReadAsync($"nonexistent-{Guid.NewGuid():N}", 0))
			count++;

		count.ShouldBe(0);
	}

	// -------- Write --------

	[Test]
	public async Task WriteAsync_Writes_Event_To_Stream(CancellationToken ct) {
		var streamName = $"test-write-{Guid.NewGuid():N}";
		var client = new KontextClient(SystemClient);

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
	public async Task WriteAsync_Multiple_Events_Append_To_Same_Stream(CancellationToken ct) {
		var streamName = $"test-write-{Guid.NewGuid():N}";
		var client = new KontextClient(SystemClient);

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
		var client = new KontextClient(SystemClient);

		var events = new[] {
			new Event(Guid.NewGuid(), "EventA", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { key = "value1" })),
			new Event(Guid.NewGuid(), "EventB", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(new { key = "value2" })),
		};
		await SystemClient.Writing.WriteEvents(streamName, events);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(10));

		var received = new List<KontextEvent>();
		await foreach (var evt in client.SubscribeToAll(null, null, cts.Token)) {
			if (evt.StreamName == streamName)
				received.Add(evt);

			if (received.Count >= 2)
				break;
		}

		received.Count.ShouldBe(2);

		received[0].EventType.ShouldBe("EventA");
		received[0].EventNumber.ShouldBe(0UL);
		received[0].StreamName.ShouldBe(streamName);
		received[0].CommitPosition.ShouldBeGreaterThan(0UL);

		received[1].EventType.ShouldBe("EventB");
		received[1].EventNumber.ShouldBe(1UL);
	}

	[Test]
	public async Task SubscribeToAll_Resumes_From_Checkpoint(CancellationToken ct) {
		var streamName = $"test-resume-{Guid.NewGuid():N}";
		var client = new KontextClient(SystemClient);

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "First", isJson: true, "{}", "{}"),
		]);

		using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts1.CancelAfter(TimeSpan.FromSeconds(10));

		(ulong Commit, ulong Prepare)? checkpoint = null;
		await foreach (var evt in client.SubscribeToAll(null, null, cts1.Token)) {
			if (evt.StreamName == streamName) {
				checkpoint = (evt.CommitPosition, evt.PreparePosition);
				break;
			}
		}

		checkpoint.ShouldNotBeNull();

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "Second", isJson: true, "{}", "{}"),
		]);

		using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts2.CancelAfter(TimeSpan.FromSeconds(10));

		var received = new List<KontextEvent>();
		await foreach (var evt in client.SubscribeToAll(checkpoint, null, cts2.Token)) {
			if (evt.StreamName == streamName)
				received.Add(evt);

			if (received.Count >= 1)
				break;
		}

		received.Count.ShouldBe(1);
		received[0].EventType.ShouldBe("Second");
	}

	[Test]
	public async Task SubscribeToAll_Event_Data_Is_Accessible(CancellationToken ct) {
		var streamName = $"test-data-{Guid.NewGuid():N}";
		var client = new KontextClient(SystemClient);
		var payload = new { name = "test", count = 42 };

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "DataEvent", isJson: true,
				JsonSerializer.SerializeToUtf8Bytes(payload)),
		]);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(10));

		await foreach (var evt in client.SubscribeToAll(null, null, cts.Token)) {
			if (evt.StreamName != streamName) continue;

			evt.Data.Length.ShouldBeGreaterThan(0);
			var json = JsonDocument.Parse(evt.Data);
			json.RootElement.GetProperty("name").GetString().ShouldBe("test");
			json.RootElement.GetProperty("count").GetInt32().ShouldBe(42);
			break;
		}
	}
}
