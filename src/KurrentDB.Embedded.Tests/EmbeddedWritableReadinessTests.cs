// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;

namespace KurrentDB.Embedded.Tests;

[Timeout(180_000)]
public class EmbeddedWritableReadinessTests {
	[Test]
	public async Task fresh_start_accepts_an_immediate_write_without_retry(CancellationToken cancellationToken) {
		for (var attempt = 0; attempt < 5; attempt++) {
			var directory = TestPaths.NewDataDirectory();
			try {
				await using var db = new EmbeddedKurrentDB(TestPaths.Options(directory) with {
					DatabaseOptions = new Dictionary<string, string?> {
						["KurrentDB:ChunkSize"] = "16777216",
						["KurrentDB:MaxAppendSize"] = "1048576",
						["KurrentDB:MaxAppendEventSize"] = "1048576",
						["KurrentDB:CachedChunks"] = "1",
						["KurrentDB:SqlEngineMemoryLimit"] = "67108864"
					}
				});
				await db.StartAsync(cancellationToken);
				await using var client = new KurrentDBClient(db.CreateClientSettings());
				var data = new EventData(Uuid.NewUuid(), "ready", "{}"u8.ToArray());
				await client.AppendToStreamAsync("readiness", StreamState.NoStream, [data],
					deadline: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
				var events = new List<ResolvedEvent>();
				await foreach (var item in client.ReadStreamAsync(Direction.Forwards, "readiness", StreamPosition.Start,
					cancellationToken: cancellationToken))
					events.Add(item);
				events.Count.ShouldBe(1);
				events[0].Event.EventId.ShouldBe(data.EventId);
			} finally {
				TestPaths.Delete(directory);
			}
		}
	}
}
