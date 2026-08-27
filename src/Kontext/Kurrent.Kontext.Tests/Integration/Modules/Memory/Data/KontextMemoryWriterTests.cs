// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Quack;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Tests.Modules.Memory.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextMemoryWriter"/> against a REAL DuckDB + Lance engine:
/// each test fabricates <see cref="SurgeRecord"/>s from the proto events and applies them in
/// BATCHES through <c>ProjectAsync</c> — the same unit of work the projector service hands over
/// per <c>ReadBatches</c> window (no consumer, no checkpoint loop; those belong to Surge).
/// Reads are asserted through <see cref="KontextMemoryDataStore"/>, plus direct SQL for the columns
/// the store deliberately never exposes (log_position, embedding, cited_memory_ids).
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class KontextMemoryWriterTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask projects_a_batch_of_retained_memories_in_one_transaction(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var retained          = NewRetained("m1", "first belief", Base);
		var expectedMemory    = retained.Memories[0].Memory;
		var expectedEmbedding = await KontextTestEmbeddings.Embed(expectedMemory.Content, cancellationToken);

		// Act — two retains, ONE batch, one write transaction.
		await Project(writer,
			CreateRecord(retained, position: 100),
			CreateRecord(NewRetained("m2", "second belief", Base.AddHours(1)), position: 200));

		// Assert — the store round-trips the first row's contract fields.
		var stored = await store.GetAsync("m1");

		await Assert.That(stored).IsNotNull();
		await Assert.That(stored!.MemoryType).IsEqualTo(expectedMemory.MemoryType);
		await Assert.That(stored.Content).IsEqualTo(expectedMemory.Content);
		await Assert.That(stored.Importance).IsEqualTo(expectedMemory.Importance);
		await Assert.That(stored.Tags.Count).IsEqualTo(2);
		await Assert.That(stored.Evidence.Count).IsEqualTo(1);
		await Assert.That(stored.Evidence[0].Memory.Id).IsEqualTo("cited-1");
		await Assert.That(stored.ContentTime!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(Base.AddHours(-24));
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(stored.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(stored.SupersededAt).IsNull();
		await Assert.That(stored.SupersededBy).IsEqualTo("");

		// Assert — the write-side stamps for both rows.
		var (logPosition, embeddingMatches, citesSource) = ReadProjectionStamp(dataSources, "m1", expectedEmbedding, citedId: "cited-1");

		await Assert.That(logPosition).IsEqualTo(100UL);
		await Assert.That(embeddingMatches).IsTrue();
		await Assert.That(citesSource).IsTrue();
		await Assert.That(ReadLogPosition(dataSources, "m2")).IsEqualTo(200UL);
	}

	[Test]
	public async ValueTask batch_and_checkpoint_commit_and_revert_together(CancellationToken cancellationToken) {
		// Arrange — the projector service's exact loop shape: writer MERGE + checkpoint MERGE
		// in one transaction on the lance-redirected connection. A lance-writing transaction
		// cannot touch any other attached catalog, so the checkpoint rides the redirection.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		await using var connection = dataSources.OpenLanceWriter();

		var writer      = NewWriter(connection);
		var checkpoints = new KontextCheckpointStore("memories-writer-test");
		checkpoints.EnsureSchema(connection);

		// Act — committed batch.
		using (var tx = connection.BeginTransaction()) {
			await Project(writer, CreateRecord(NewRetained("m1", "first belief", Base), position: 100));
			checkpoints.Store(connection, RecordPosition.ForLog(100));
			tx.CommitOnDispose();
		}

		var afterCommit = checkpoints.Load(connection);

		// Act — rolled-back batch: dispose without commit.
		using (connection.BeginTransaction()) {
			await Project(writer, CreateRecord(NewRetained("m2", "second belief", Base.AddHours(1)), position: 205));
			checkpoints.Store(connection, RecordPosition.ForLog(205));
		}

		var afterRollback = checkpoints.Load(connection);

		// Assert — data and checkpoint advanced together, then reverted together.
		await Assert.That((ulong?)afterCommit).IsEqualTo(100UL);
		await Assert.That((ulong?)afterRollback).IsEqualTo(100UL);
		await Assert.That(ReadRowCount(dataSources, "m1")).IsEqualTo(1L);
		await Assert.That(ReadRowCount(dataSources, "m2")).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask retain_replay_across_batches_leaves_state_unchanged(CancellationToken cancellationToken) {
		// Arrange — the same record applied in two successive batches: exactly what a crash
		// between an applied batch and its checkpoint produces on restart. The matched arm
		// rewrites the content columns with the same values — the state cannot change.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);
		var       record = CreateRecord(NewRetained("m1", "first belief", Base), position: 100);

		// Act
		await Project(writer, record);
		await Project(writer, record);

		// Assert — one row, intact, still stamped with the original position.
		await Assert.That(ReadRowCount(dataSources, "m1")).IsEqualTo(1L);
		await Assert.That((await store.GetAsync("m1"))!.Content).IsEqualTo("first belief");
		await Assert.That(ReadLogPosition(dataSources, "m1")).IsEqualTo(100UL);
	}

	[Test]
	public async ValueTask retain_replay_refreshes_the_embedding_in_place(CancellationToken cancellationToken) {
		// Arrange — the original pass wrote m1 with its first body.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		await Project(NewWriter(connection), CreateRecord(NewRetained("m1", "first belief", Base), position: 100));

		var revisedContent  = "a materially different belief";
		var expectedVector  = await KontextTestEmbeddings.Embed(revisedContent, cancellationToken);

		// Act — replay the same id at the same position with a revised body: the matched arm owns
		// the content columns, so it must rewrite content and embedding together rather than
		// leaving the first pass's vector behind.
		await Project(NewWriter(connection), CreateRecord(NewRetained("m1", revisedContent, Base), position: 100));

		// Assert — still one row, its embedding refreshed to the revised body's vector.
		var (logPosition, embeddingMatches, _) = ReadProjectionStamp(dataSources, "m1", expectedVector, citedId: "cited-1");

		await Assert.That(ReadRowCount(dataSources, "m1")).IsEqualTo(1L);
		await Assert.That(embeddingMatches).IsTrue();
		await Assert.That(logPosition).IsEqualTo(100UL);
	}

	[Test]
	public async ValueTask retain_replay_does_not_resurrect_folded_lifecycle(CancellationToken cancellationToken) {
		// Arrange — m1 superseded by m2 in a later batch, then m1's retained record replays.
		// An overwrite-on-match implementation would briefly resurrect m1 here.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var supersededAt = Base.AddHours(2);
		var m1Record     = CreateRecord(NewRetained("m1", "old belief", Base), position: 100);

		await Project(writer, m1Record);
		await Project(writer, CreateRecord(NewRetained("m2", "new belief", supersededAt, supersedes: "m1"), position: 200));

		// Act — replay m1's retained record.
		await Project(writer, m1Record);

		// Assert — the supersession fold survives the replay, and no duplicate row appeared.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededAt.ToDateTimeOffset()).IsEqualTo(supersededAt);
		await Assert.That(old.SupersededBy).IsEqualTo("m2");
		await Assert.That(ReadRowCount(dataSources, "m1")).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask same_batch_supersession_folds_prior_rows(CancellationToken cancellationToken) {
		// Arrange — m1 and its successor m2 arrive in the SAME batch: the insert leg runs
		// before the folds, so the supersession must land on the row inserted moments earlier.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var supersededAt = Base.AddHours(2);

		// Act
		await Project(writer,
			CreateRecord(NewRetained("m1", "old belief", Base), position: 100),
			CreateRecord(NewRetained("m2", "new belief", supersededAt, supersedes: "m1"), position: 200));

		// Assert — m1 is marked superseded by m2 and re-stamped with the superseding position.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededAt.ToDateTimeOffset()).IsEqualTo(supersededAt);
		await Assert.That(old.SupersededBy).IsEqualTo("m2");
		await Assert.That(ReadLogPosition(dataSources, "m1")).IsEqualTo(200UL);

		// Assert — the successor carries the supersedes edge, keeping the lineage symmetric.
		var successor = await store.GetAsync("m2");

		await Assert.That(successor!.Supersedes).Contains("m1");
		await Assert.That(successor.SupersededAt).IsNull();
	}

	[Test]
	public async ValueTask a_later_supersession_never_repoints_an_already_superseded_memory(CancellationToken cancellationToken) {
		// Arrange — m1 superseded by m2, then m3 arrives in a LATER batch claiming m1 as well.
		// Retain rejects that call, so this is the second line of defence: a memory carries ONE
		// successor, and letting m3 take it would leave m2 listing a target it no longer owns.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var store  = new KontextMemoryDataStore(dataSources);

		var firstAt  = Base.AddHours(2);
		var secondAt = Base.AddHours(4);

		await Project(writer, CreateRecord(NewRetained("m1", "old belief", Base), position: 100));
		await Project(writer, CreateRecord(NewRetained("m2", "new belief", firstAt, supersedes: "m1"), position: 200));

		// Act
		await Project(writer, CreateRecord(NewRetained("m3", "newer belief", secondAt, supersedes: "m1"), position: 300));

		// Assert — first writer wins on both lifecycle columns.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededBy).IsEqualTo("m2");
		await Assert.That(old.SupersededAt.ToDateTimeOffset()).IsEqualTo(firstAt);
	}

	[Test]
	public async ValueTask two_supersessions_of_one_memory_in_a_batch_keep_the_first(CancellationToken cancellationToken) {
		// Arrange — the same conflict inside ONE consumed batch, where the MERGE cannot see it: the
		// batch folds to one row state per id before the statement runs, so the guard has to hold in
		// the fold as well.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var store  = new KontextMemoryDataStore(dataSources);

		var firstAt  = Base.AddHours(2);
		var secondAt = Base.AddHours(4);

		await Project(writer, CreateRecord(NewRetained("m1", "old belief", Base), position: 100));

		// Act — both successors claim m1, in log order.
		await Project(writer,
			CreateRecord(NewRetained("m2", "new belief", firstAt, supersedes: "m1"), position: 200),
			CreateRecord(NewRetained("m3", "newer belief", secondAt, supersedes: "m1"), position: 300));

		// Assert — events fold in log order, so the first supersession seen is the one that won.
		var old = await store.GetAsync("m1");

		await Assert.That(old!.SupersededBy).IsEqualTo("m2");
		await Assert.That(old.SupersededAt.ToDateTimeOffset()).IsEqualTo(firstAt);
	}

	[Test]
	public async ValueTask recall_resets_the_recency_clock_and_the_latest_recall_wins(CancellationToken cancellationToken) {
		// Arrange — a memory whose recency clock starts at its retention instant.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var firstRecallAt = Base.AddHours(5);
		var lastRecallAt  = Base.AddHours(7);

		await Project(writer, CreateRecord(NewRetained("m1", "first belief", Base), position: 100));

		// Act — two recalls of the same memory in ONE batch: reconsolidation must keep the
		// LATEST recall as the recency clock, not the first.
		await Project(writer,
			CreateRecord(NewRecalled("m1", firstRecallAt), position: 200),
			CreateRecord(NewRecalled("m1", lastRecallAt), position: 300));

		// Assert
		var stored = await store.GetAsync("m1");

		await Assert.That(stored!.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(lastRecallAt);
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(ReadLogPosition(dataSources, "m1")).IsEqualTo(300UL);
	}

	[Test]
	public async ValueTask reinforce_resets_the_recency_clock_of_every_memory_it_names(CancellationToken cancellationToken) {
		// Arrange — two memories retained at the same instant, only one of which gets reinforced.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var store  = new KontextMemoryDataStore(dataSources);

		var usedAt = Base.AddHours(9);

		await Project(writer,
			CreateRecord(NewRetained("m1", "the belief that helped", Base), position: 100),
			CreateRecord(NewRetained("m2", "the belief that did not", Base), position: 110));

		// Act — MemoriesReinforced is what `reinforce` appends. Without a projector case for it the
		// event would land in the log and move nothing. Twice, in separate batches: the clock is
		// terminal (latest wins) while the count accumulates.
		await Project(writer, CreateRecord(NewReinforced(Base.AddHours(3), "m1"), position: 200));
		await Project(writer, CreateRecord(NewReinforced(usedAt, "m1"), position: 300));

		// Assert — only the named memory moves; the other keeps the clock it was retained with.
		var reinforced = await store.GetAsync("m1");
		var untouched  = await store.GetAsync("m2");

		await Assert.That(reinforced!.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(usedAt);
		await Assert.That(reinforced.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(ReadAccessCount(dataSources, "m1")).IsEqualTo(2L);

		await Assert.That(untouched!.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(ReadAccessCount(dataSources, "m2")).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask projects_every_memory_of_one_retain_call(CancellationToken cancellationToken) {
		// Arrange — ONE event carrying a three-memory retain call, which is what `retain` emits for
		// a batch. Its last memory supersedes the first, so the intra-event ordering guarantee is
		// exercised too: the fold has to find a row this same event inserted.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var retained = NewRetainedBatch(Base,
			("b1", "first observation", []),
			("b2", "second observation", []),
			("b3", "consolidated belief", ["b1"]));

		var expectedIds = new List<string> { "b1", "b2", "b3" };

		// Act
		await Project(writer, CreateRecord(retained, position: 100));

		// Assert — every memory of the call landed, each stamped with the event's position.
		var stored = await store.GetAsync(expectedIds.ToArray()).ToListAsync();

		await Assert.That(stored.Select(memory => memory.MemoryId).Order().ToList()).IsEquivalentTo(expectedIds);
		await Assert.That(ReadLogPosition(dataSources, "b3")).IsEqualTo(100UL);

		// Assert — they share the call's single retention instant.
		foreach (var memory in stored)
			await Assert.That(memory.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);

		// Assert — the intra-call supersession folded onto a row inserted by this very event.
		var superseded = stored.Single(memory => memory.MemoryId == "b1");

		await Assert.That(superseded.SupersededBy).IsEqualTo("b3");
		await Assert.That(superseded.SupersededAt.ToDateTimeOffset()).IsEqualTo(Base);

		// Assert — an untouched sibling keeps its live lifecycle.
		await Assert.That(stored.Single(memory => memory.MemoryId == "b2").SupersededAt).IsNull();
	}

	[Test]
	public async ValueTask a_memory_born_superseded_in_one_batch_carries_its_terminal_state(CancellationToken cancellationToken) {
		// Arrange — the conjunction case: m1 does not exist, and ONE batch retains it AND
		// supersedes it (via m2). The single MERGE must insert the row already carrying the
		// full terminal state — no intermediate live window ever exists.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var       store  = new KontextMemoryDataStore(dataSources);

		var supersededAt = Base.AddHours(2);

		// Act
		await Project(writer,
			CreateRecord(NewRetained("m1", "old belief", Base), position: 100),
			CreateRecord(NewRetained("m2", "new belief", supersededAt, supersedes: "m1"), position: 200));

		// Assert — one row, both lifecycle facets folded at birth, stamped with the last touch.
		var stored = await store.GetAsync("m1");

		await Assert.That(ReadRowCount(dataSources, "m1")).IsEqualTo(1L);
		await Assert.That(stored!.SupersededBy).IsEqualTo("m2");
		await Assert.That(stored.SupersededAt.ToDateTimeOffset()).IsEqualTo(supersededAt);
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);
		await Assert.That(ReadLogPosition(dataSources, "m1")).IsEqualTo(200UL);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>One MemoriesRetained event carrying several memories, in request order.</summary>
	static MemoryContracts.MemoriesRetained NewRetainedBatch(
		DateTimeOffset retainedAt,
		params (string MemoryId, string Content, string[] Supersedes)[] memories
	) {
		var retained = new MemoryContracts.MemoriesRetained { RetainedAt = Timestamp.FromDateTimeOffset(retainedAt) };

		foreach (var (memoryId, content, supersedes) in memories)
			retained.Memories.Add(new MemoryContracts.MemoriesRetained.Types.RetainedMemory {
				MemoryId = memoryId,
				Memory = new MemoryContracts.Memory {
					MemoryType = MemoryContracts.MemoryType.Fact,
					Content    = content,
					Importance = MemoryContracts.MemoryImportance.Normal,
					Supersedes = { supersedes },
				},
			});

		return retained;
	}

	/// <summary>A single-memory MemoriesRetained event: two tags, evidence citing "cited-1", a content-time window.</summary>
	static MemoryContracts.MemoriesRetained NewRetained(string memoryId, string content, DateTimeOffset retainedAt, params string[] supersedes) {
		var memory = new MemoryContracts.Memory {
			MemoryType = MemoryContracts.MemoryType.Fact,
			Content    = content,
			Importance = MemoryContracts.MemoryImportance.High,
			Reasoning  = "because the tests say so",
			Evidence   = { new MemoryContracts.Evidence { Memory = new() { Id = "cited-1" } } },
			Tags       = { new MemoryContracts.Tag { Scope = "work", Value = "alpha" }, new MemoryContracts.Tag { Value = "research" } },
			ContentTime   = new MemoryContracts.TemporalContext {
				PerceivedStart = Timestamp.FromDateTimeOffset(retainedAt.AddHours(-24)),
				PerceivedEnd   = Timestamp.FromDateTimeOffset(retainedAt.AddHours(24))
			},
			Supersedes = { supersedes }
		};

		return new() {
			Memories   = { new MemoryContracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory } },
			RetainedAt = Timestamp.FromDateTimeOffset(retainedAt),
		};
	}

	static MemoryContracts.MemoriesReinforced NewReinforced(DateTimeOffset reinforcedAt, params string[] memoryIds) {
		var reinforced = new MemoryContracts.MemoriesReinforced { ReinforcedAt = Timestamp.FromDateTimeOffset(reinforcedAt) };
		reinforced.MemoryIds.AddRange(memoryIds);
		return reinforced;
	}

	static MemoryContracts.MemoriesRecalled NewRecalled(string memoryId, DateTimeOffset recalledAt) => new() {
		QueryId    = Guid.NewGuid().ToString(),
		Query      = "query",
		Memories   = { new MemoryContracts.ScoredMemory { MemoryId = memoryId } },
		RecalledAt = Timestamp.FromDateTimeOffset(recalledAt)
	};

	// The same shape as the projection tests' CreateRecord: the writer switches on Value and
	// never reads Data, so raw proto bytes and a cosmetic SchemaInfo are enough.
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

	/// <summary>Applies one batch through the writer, exactly like the projector's batch loop would.</summary>
	static async ValueTask Project(KontextMemoryWriter writer, params SurgeRecord[] batch) =>
		await writer.ProjectAsync(batch, CancellationToken.None);

	static KontextMemoryWriter NewWriter(DuckDBAdvancedConnection connection) =>
		new(connection, KontextTestEmbeddings.Model, KontextTestEmbeddings.Options);

	/// <summary>Reads the write-side columns the store never surfaces, in one round trip.</summary>
	static (ulong LogPosition, bool EmbeddingMatches, bool CitesSource) ReadProjectionStamp(
		KontextDataSource dataSource, string memoryId, float[] expectedEmbedding, string citedId
	) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				$"""
				SELECT log_position,
				       embedding = CAST($expected_embedding AS FLOAT[{KontextIndexConstants.VectorsDimension}]),
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
		});

	static long ReadRowCount(KontextDataSource dataSource, string memoryId) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT count(*) FROM ldb.main.memories WHERE memory_id = $memory_id";
			command.Parameters.Add(new("memory_id", memoryId));
			return (long)command.ExecuteScalar()!;
		});

	static long ReadAccessCount(KontextDataSource dataSource, string memoryId) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT access_count FROM ldb.main.memories WHERE memory_id = $memory_id";
			command.Parameters.Add(new("memory_id", memoryId));
			return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		});

	static ulong ReadLogPosition(KontextDataSource dataSource, string memoryId) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT log_position FROM ldb.main.memories WHERE memory_id = $memory_id";
			command.Parameters.Add(new("memory_id", memoryId));
			return Convert.ToUInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
		});

	static KontextDataSource NewDataSources(string dir) =>
		MemorySeeding.NewDataSources(dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-memory-writer-tests", Guid.NewGuid().ToString("N"));

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
