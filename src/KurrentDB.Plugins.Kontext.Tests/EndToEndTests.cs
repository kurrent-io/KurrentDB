using System.Text.Json;
using Kurrent.Kontext;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class EndToEndTests {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	ISystemClient SystemClient => NodeShim.Node.Services.GetRequiredService<ISystemClient>();

	[Test]
	public async Task Write_And_Read_Round_Trip() {
		var streamName = $"e2e-{Guid.NewGuid():N}";
		var client = new KontextClient(SystemClient);

		var data = JsonSerializer.SerializeToUtf8Bytes(new {
			fact = "Alice ordered 50 widgets on 2026-01-15",
			sourceEvents = new[] { "orders-123::0", "orders-123::1" },
		});
		await client.WriteAsync(streamName, "FactRetained", data);

		var results = new List<EventResult>();
		await foreach (var evt in client.ReadAsync(streamName, 0))
			results.Add(evt);

		results.Count.ShouldBe(1);
		results[0].EventType.ShouldBe("FactRetained");
		results[0].Stream.ShouldBe(streamName);
		results[0].Data.ShouldNotBeNull();
		results[0].Data!.Value.GetProperty("fact").GetString()
			.ShouldBe("Alice ordered 50 widgets on 2026-01-15");
	}

	[Test]
	public async Task Reader_Handles_Non_Json_Events() {
		var streamName = $"e2e-binary-{Guid.NewGuid():N}";

		await SystemClient.Writing.WriteEvents(streamName, [
			new Event(Guid.NewGuid(), "BinaryEvent", isJson: false, new byte[] { 0x01, 0x02, 0x03 }),
		]);

		var client = new KontextClient(SystemClient);

		var results = new List<EventResult>();
		await foreach (var evt in client.ReadAsync(streamName, 0))
			results.Add(evt);

		results.Count.ShouldBe(1);
		results[0].EventType.ShouldBe("BinaryEvent");
		results[0].Data.ShouldBeNull();
	}
}
