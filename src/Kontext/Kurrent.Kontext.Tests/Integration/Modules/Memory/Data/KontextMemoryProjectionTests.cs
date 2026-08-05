// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Memory.Data;
using Kurrent.Quack;
using Kurrent.Surge;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Projectors;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.AI;
using TUnit.Assertions.Enums;

namespace Kurrent.Kontext.Tests.Modules.Memory.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextMemoryProjection"/> against a REAL DuckDB + Lance
/// engine: each test fabricates <see cref="SurgeRecord"/>s from the proto events and applies them
/// with <c>ProjectRecord</c> — the same pattern as the schema registry's <c>ProjectionsTests</c>,
/// minus the vnode fixture (no consumer, no checkpoint loop; those belong to Surge). Reads are
/// asserted through <see cref="KontextDataStore"/>, plus direct SQL for the columns the store
/// deliberately never exposes (log_position, embedding, cited_memory_ids).
/// </summary>
[Category("Integration")]
public class KontextMemoryProjectionTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask memory_retained_projects_the_full_row() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);

		var retained          = NewRetained("m1", "first belief", Base);
		var expectedMemory    = retained.Memories[0].Memory;
		var expectedEmbedding = FakeEmbeddingGenerator.Embed(expectedMemory.Content);

		// Act
		await Apply(pool, projection, CreateRecord(retained, position: 100));

		// Assert — the store round-trips every contract field the projection wrote.
		var stored = await store.GetAsync("m1");

		await Assert.That(stored).IsNotNull();
		await Assert.That(stored!.MemoryType).IsEqualTo(expectedMemory.MemoryType);
		await Assert.That(stored.Content).IsEqualTo(expectedMemory.Content);
		await Assert.That(stored.Importance).IsEqualTo(expectedMemory.Importance);
		// Compared in encoded wire form: TUnit's structural equivalence trips on proto presence
		// bits (a decoded tag carries Scope = "" explicitly; the expected one never set it),
		// and the encoded strings also pin the shared codec.
		await Assert.That(stored.Tags.Select(KontextDataStore.EncodeTag).ToList())
			.IsEquivalentTo(["work:alpha", "research"], CollectionOrdering.Matching);
		await Assert.That(stored.Evidence).IsEqualTo(expectedMemory.Evidence);
		await Assert.That(stored.Validity).IsEqualTo(expectedMemory.Validity);
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(stored.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(stored.RetractedAt).IsNull();
		await Assert.That(stored.SupersededAt).IsNull();
		await Assert.That(stored.SupersededBy).IsEqualTo("");

		// Assert — the columns the store never surfaces: the row is stamped with the record's
		// commit position, the embedding is exactly what the generator produced, and the
		// evidence's memory citation landed in cited_memory_ids.
		var (logPosition, embeddingMatches, citesSource) = ReadProjectionStamp(pool, "m1", expectedEmbedding, citedId: "cited-1");

		await Assert.That(logPosition).IsEqualTo(100UL);
		await Assert.That(embeddingMatches).IsTrue();
		await Assert.That(citesSource).IsTrue();
	}

	[Test]
	public async ValueTask retain_replay_is_a_no_op() {
		// Arrange — the same record applied twice: exactly what a crash between an applied
		// record and the checkpoint flush produces on restart.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);
		var record     = CreateRecord(NewRetained("m1", "first belief", Base), position: 100);

		// Act
		await Apply(pool, projection, record);
		await Apply(pool, projection, record);

		// Assert — one row, intact, still stamped with the original position.
		await Assert.That(ReadRowCount(pool, "m1")).IsEqualTo(1L);
		await Assert.That((await store.GetAsync("m1"))!.Content).IsEqualTo("first belief");
		await Assert.That(ReadLogPosition(pool, "m1")).IsEqualTo(100UL);
	}

	[Test]
	public async ValueTask retain_replay_does_not_resurrect_folded_lifecycle() {
		// Arrange — m1 superseded by m2, then m1's retained record replays (a crash landed
		// the checkpoint before both records). An overwrite-on-match implementation would
		// briefly resurrect m1 here; insert-if-absent must leave the fold untouched.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);

		var supersededAt = Base.AddHours(2);
		var m1Record     = CreateRecord(NewRetained("m1", "old belief", Base), position: 100);

		await Apply(pool, projection, m1Record);
		await Apply(pool, projection, CreateRecord(NewRetained("m2", "new belief", supersededAt, supersedes: "m1"), position: 200));

		// Act — replay m1's retained record.
		await Apply(pool, projection, m1Record);

		// Assert — the supersession fold survives the replay, and no duplicate row appeared.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededAt.ToDateTimeOffset()).IsEqualTo(supersededAt);
		await Assert.That(old.SupersededBy).IsEqualTo("m2");
		await Assert.That(ReadRowCount(pool, "m1")).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask retain_folds_supersession_into_prior_rows() {
		// Arrange — m1 exists; m2 arrives superseding it.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);

		var supersededAt = Base.AddHours(2);

		// Act
		await Apply(pool, projection, CreateRecord(NewRetained("m1", "old belief", Base), position: 100));
		await Apply(pool, projection, CreateRecord(NewRetained("m2", "new belief", supersededAt, supersedes: "m1"), position: 200));

		// Assert — m1 is marked superseded by m2 at m2's retention instant, and the fold
		// re-stamped m1's row with the superseding record's position.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededAt.ToDateTimeOffset()).IsEqualTo(supersededAt);
		await Assert.That(old.SupersededBy).IsEqualTo("m2");
		await Assert.That(ReadLogPosition(pool, "m1")).IsEqualTo(200UL);

		// Assert — the successor carries the supersedes edge, keeping the lineage symmetric.
		var successor = await store.GetAsync("m2");

		await Assert.That(successor!.Supersedes).Contains("m1");
		await Assert.That(successor.SupersededAt).IsNull();
	}

	[Test]
	public async ValueTask memory_retracted_marks_every_cascaded_row() {
		// Arrange — two live memories; the retraction cascades over both.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);

		var retractedAt = Base.AddHours(3);

		await Apply(pool, projection, CreateRecord(NewRetained("m1", "first belief", Base), position: 100));
		await Apply(pool, projection, CreateRecord(NewRetained("m2", "derived belief", Base.AddHours(1)), position: 200));

		// Act
		var retracted = new Contracts.MemoryRetracted {
			MemoryId            = "m1",
			Reason              = "test cascade",
			RetractedMemoryIds  = { "m1", "m2" },
			RetractedAt         = Timestamp.FromDateTimeOffset(retractedAt)
		};

		await Apply(pool, projection, CreateRecord(retracted, position: 300));

		// Assert — both rows carry the retraction instant, and listings hide them.
		await Assert.That((await store.GetAsync("m1"))!.RetractedAt.ToDateTimeOffset()).IsEqualTo(retractedAt);
		await Assert.That((await store.GetAsync("m2"))!.RetractedAt.ToDateTimeOffset()).IsEqualTo(retractedAt);

		var listed = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		await Assert.That(listed).IsEmpty();
	}

	[Test]
	public async ValueTask memories_recalled_resets_the_recency_clock() {
		// Arrange — a memory whose recency clock starts at its retention instant.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await NewSchema(pool).CreateAsync();

		var projection = new KontextMemoryProjection(new FakeEmbeddingGenerator());
		var store      = new KontextDataStore(pool);

		var recalledAt = Base.AddHours(5);

		await Apply(pool, projection, CreateRecord(NewRetained("m1", "first belief", Base), position: 100));

		// Act — reconsolidation: the recall resets last_accessed_at to recalled_at.
		var recalled = new Contracts.MemoriesRecalled {
			QueryId    = Guid.NewGuid().ToString(),
			Query      = "first",
			Memories   = { new Contracts.ScoredMemory { MemoryId = "m1" } },
			RecalledAt = Timestamp.FromDateTimeOffset(recalledAt)
		};

		await Apply(pool, projection, CreateRecord(recalled, position: 200));

		// Assert
		var stored = await store.GetAsync("m1");

		await Assert.That(stored!.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(recalledAt);
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>A single-memory MemoriesRetained event: two tags, evidence citing "cited-1", a validity window.</summary>
	static Contracts.MemoriesRetained NewRetained(string memoryId, string content, DateTimeOffset retainedAt, params string[] supersedes) {
		var memory = new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = content,
			Importance = Contracts.MemoryImportance.High,
			Reasoning  = "because the tests say so",
			Evidence   = { new Contracts.Evidence { Memory = new() { Id = "cited-1" } } },
			Tags       = { new Contracts.Tag { Scope = "work", Value = "alpha" }, new Contracts.Tag { Value = "research" } },
			Validity   = new Contracts.TemporalContext {
				PerceivedStart = Timestamp.FromDateTimeOffset(retainedAt.AddHours(-24)),
				PerceivedEnd   = Timestamp.FromDateTimeOffset(retainedAt.AddHours(24))
			},
			Supersedes = { supersedes }
		};

		return new() {
			Memories   = { new Contracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory } },
			RetainedAt = Timestamp.FromDateTimeOffset(retainedAt),
		};
	}

	// The same shape as the schema registry's ClusterVNodeTestContext.CreateRecord, minus the
	// Surge schema serializer: the projection routes on ValueType and hands Value to the handler
	// without ever reading Data, so raw proto bytes and a cosmetic SchemaInfo are enough.
	static SurgeRecord CreateRecord<T>(T message, ulong position) where T : IMessage<T> =>
		new() {
			Id         = Guid.NewGuid(),
			Position   = RecordPosition.ForLog(position),
			Timestamp  = Base.UtcDateTime,
			SchemaInfo = new SchemaInfo($"$kontext-{typeof(T).Name.ToLowerInvariant()}", SchemaDataFormat.Json),
			Data       = message.ToByteArray(),
			Value      = message,
			ValueType  = typeof(T),
			SequenceId = position,
			Headers    = new Headers()
		};

	/// <summary>
	/// Applies one record through the projection, exactly like the projector's consume loop would.
	/// Surge's own provider wraps the Kontext pool directly — <see cref="KontextConnectionPool"/>
	/// extends Quack's <c>DuckDBConnectionPool</c>, so every provider connection carries the
	/// pool's Lance bootstrap. This is the same pairing the production wiring will use.
	/// </summary>
	static async ValueTask Apply(KontextConnectionPool pool, KontextMemoryProjection projection, SurgeRecord record) {
		IDuckDBConnectionProvider provider = new DuckDBAdvancedConnectionProvider(pool);

		await projection.ProjectRecord(new ProjectionContext<IDuckDBConnectionProvider>(
			_ => ValueTask.FromResult(provider), record, CancellationToken.None));
	}

	/// <summary>Reads the write-side columns the store never surfaces, in one round trip.</summary>
	static (ulong LogPosition, bool EmbeddingMatches, bool CitesSource) ReadProjectionStamp(
		KontextConnectionPool pool, string memoryId, float[] expectedEmbedding, string citedId
	) {
		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				SELECT log_position,
				       embedding = CAST($expected_embedding AS FLOAT[4]),
				       list_contains(cited_memory_ids, $cited_id)
				FROM ldb.main.memories
				WHERE memory_id = $memory_id
				""";
			command.Parameters.Add(new("expected_embedding", expectedEmbedding));
			command.Parameters.Add(new("cited_id", citedId));
			command.Parameters.Add(new("memory_id", memoryId));

			using var reader = command.ExecuteReader();
			reader.Read();

			return (Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetBoolean(1), reader.GetBoolean(2));
		}
	}

	static long ReadRowCount(KontextConnectionPool pool, string memoryId) {
		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT count(*) FROM ldb.main.memories WHERE memory_id = $memory_id";
			command.Parameters.Add(new("memory_id", memoryId));
			return (long)command.ExecuteScalar()!;
		}
	}

	static ulong ReadLogPosition(KontextConnectionPool pool, string memoryId) {
		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT log_position FROM ldb.main.memories WHERE memory_id = $memory_id";
			command.Parameters.Add(new("memory_id", memoryId));
			return Convert.ToUInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		}
	}

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	// Dimension 4 matches FakeEmbeddingGenerator's vectors.
	static KontextSchema NewSchema(KontextConnectionPool pool) => new(pool, new() { Dimension = 4 });

	/// <summary>
	/// Deterministic 4-dim embeddings: a unit vector on the axis picked by the content's length,
	/// so a test can recompute the exact vector the projection wrote and compare it in SQL.
	/// </summary>
	sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
		public static float[] Embed(string content) {
			var vector = new float[4];
			vector[content.Length % 4] = 1f;
			return vector;
		}

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			var results = new GeneratedEmbeddings<Embedding<float>>();

			foreach (var value in values)
				results.Add(new Embedding<float>(Embed(value)));

			return Task.FromResult(results);
		}

		public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-projection-tests", Guid.NewGuid().ToString("N"));

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

	#endregion // Test Infrastructure
}
