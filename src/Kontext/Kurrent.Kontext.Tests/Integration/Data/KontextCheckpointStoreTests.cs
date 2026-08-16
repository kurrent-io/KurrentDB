// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Surge;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextCheckpointStore"/> against a REAL engine: the shared
/// checkpoints table in the connection's current catalog — the lance catalog on the writer
/// connection, the production shape — one row per projection key, monotonic stores.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class KontextCheckpointStoreTests {
	[Test]
	public async ValueTask loads_unset_before_any_store(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var store = new KontextCheckpointStore("fresh-projection");
		store.EnsureSchema(connection);

		// Act + Assert
		await Assert.That(store.Load(connection)).IsEqualTo(RecordPosition.Unset);
	}

	[Test]
	public async ValueTask store_then_load_round_trips(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var store    = new KontextCheckpointStore("round-trip");
		var expected = RecordPosition.ForLog(4200);

		store.EnsureSchema(connection);

		// Act
		store.Store(connection, expected);

		// Assert
		await Assert.That(store.Load(connection)).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask stale_store_is_a_no_op(CancellationToken cancellationToken) {
		// Arrange — the monotonic guard: a replayed batch writing an older position folds nothing.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextCheckpointStore("monotonic");
		var newest = RecordPosition.ForLog(500);

		store.EnsureSchema(connection);
		store.Store(connection, newest);

		// Act — the replayed, older position.
		store.Store(connection, RecordPosition.ForLog(100));

		// Assert
		await Assert.That(store.Load(connection)).IsEqualTo(newest);
	}

	[Test]
	public async ValueTask keys_are_isolated(CancellationToken cancellationToken) {
		// Arrange — two projections share the table, never each other's row.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var memories = new KontextCheckpointStore("memories");
		var entities = new KontextCheckpointStore("entities");

		memories.EnsureSchema(connection);
		entities.EnsureSchema(connection);

		var memoriesPosition = RecordPosition.ForLog(100);
		var entitiesPosition = RecordPosition.ForLog(900);

		// Act
		memories.Store(connection, memoriesPosition);
		entities.Store(connection, entitiesPosition);

		// Assert
		await Assert.That(memories.Load(connection)).IsEqualTo(memoriesPosition);
		await Assert.That(entities.Load(connection)).IsEqualTo(entitiesPosition);
	}

	static KontextDataSource NewDataSources(string dir) =>
		MemorySeeding.NewDataSources(dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-checkpoint-tests", Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public void Dispose() {
			try {
				if (Directory.Exists(Path))
					Directory.Delete(Path, recursive: true);
			} catch (IOException) {
				// Best-effort cleanup; a lingering native handle must not fail the test.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}
}
