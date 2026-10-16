// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using KurrentDB.Client;

namespace KurrentDB.Embedded.Tests;

/// <summary>
/// An embedded node listens on nothing but its socket, so nothing answers on the endpoint it advertises in
/// gossip. The auto-scavenge plugin used to start, poll and stop the scavenge of the node it runs in over
/// HTTP to that endpoint, and a single embedded node never got scavenged at all.
/// </summary>
// the schedule is a cron expression with a resolution of a minute, so the cycle can take a minute to come due
[Timeout(300_000)]
public class EmbeddedAutoScavengeTests {
	static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

	[Test]
	public async Task scavenges_the_node_on_schedule(CancellationToken cancellationToken) {
		var dataDirectory = TestPaths.NewDataDirectory();

		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));
			await db.StartAsync(cancellationToken);

			using var http = new HttpClient(EmbeddedKurrentDBTests.CreateSocketHandler(db.UnixSocketPath)) {
				BaseAddress = new Uri("http://localhost"),
			};
			await using var client = new KurrentDBClient(db.CreateClientSettings());

			var enabled = await http.GetFromJsonAsync<JsonElement>("/auto-scavenge/enabled", cancellationToken);
			enabled.GetProperty("enabled").GetBoolean().ShouldBeTrue();

			// the plugin turns configuration down until it has seen gossip saying this node is the leader
			await Eventually(async () => {
				using var response = await http.PostAsJsonAsync(
					"/auto-scavenge/configure", new { schedule = "* * * * *" }, cancellationToken);
				return response.StatusCode == HttpStatusCode.OK;
			}, cancellationToken);

			var scavengeId = await Eventually(async () => {
				var started = (await ReadAll(client, "$auto-scavenges", cancellationToken))
					.FirstOrDefault(e => e.Event.EventType == "$node-scavenge-started");

				if (started.Event is null)
					return (Guid?)null;

				using var data = JsonDocument.Parse(started.Event.Data);
				return data.RootElement.GetProperty("scavengeId").GetGuid();
			}, cancellationToken);

			await Eventually(async () =>
				(await ReadAll(client, $"$scavenges-{scavengeId}", cancellationToken))
				.Any(e => e.Event.EventType == "$scavengeCompleted"), cancellationToken);

			// and the cycle finishes, ready for the next one
			await Eventually(async () => {
				var events = await ReadAll(client, "$auto-scavenges", cancellationToken);
				return events.Any(e => e.Event.EventType == "$cluster-scavenge-completed");
			}, cancellationToken);

			var status = await http.GetFromJsonAsync<JsonElement>("/auto-scavenge/status", cancellationToken);
			status.GetProperty("state").GetString().ShouldBe("Waiting");
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}

	static async Task<List<ResolvedEvent>> ReadAll(KurrentDBClient client, string stream, CancellationToken cancellationToken) {
		var read = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, cancellationToken: cancellationToken);

		if (await read.ReadState == ReadState.StreamNotFound)
			return [];

		var events = new List<ResolvedEvent>();
		await foreach (var resolved in read)
			events.Add(resolved);
		return events;
	}

	static async Task Eventually(Func<Task<bool>> condition, CancellationToken cancellationToken) =>
		await Eventually(async () => await condition() ? true : (bool?)null, cancellationToken);

	// the test's timeout is what bounds the wait
	static async Task<T> Eventually<T>(Func<Task<T?>> attempt, CancellationToken cancellationToken) where T : struct {
		while (true) {
			if (await attempt() is { } result)
				return result;

			await Task.Delay(PollInterval, cancellationToken);
		}
	}
}
