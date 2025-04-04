// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using EventStore.LogCommon;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;
#pragma warning disable CS0162 // Unreachable code detected

namespace EventStore.Core.XUnit.Tests.Scavenge.PortedFromOldScavenge;

// old scavenge tests that derived from ScavengeTestScenario
public class ScavengeTestScenarioPortedScavengeTests : SqliteDbPerTest<ScavengeTestScenarioPortedScavengeTests> {
	[Fact]
	public async Task when_scavenging_tfchunk_with_version0_log_records_and_incomplete_chunk() {
		const byte version = LogRecordVersion.LogRecordV0;
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ES1", version: version),
					Rec.Write(t++, "ES1", version: version))
				.CompleteLastChunk()
				.Chunk(
					Rec.Write(t++, "ES2", version: version),
					Rec.Write(t++, "ES2", version: version))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepAll(),
				x.Recs[1].KeepAll(),
				x.Recs[2].KeepAll(),
			});
	}

	// temp streams not supported:
	// when_stream_is_softdeleted_and_temp_and_all_events_and_metaevents_are_in_one_chunk
	// when_stream_is_softdeleted_and_temp_but_some_events_are_in_multiple_chunks
	// when_stream_is_softdeleted_and_temp_but_some_metaevents_are_in_multiple_chunks
	// when_stream_is_softdeleted_and_temp_with_log_version_0_but_some_events_are_in_multiple_chunks
	// when_stream_is_softdeleted_with_log_record_version_0
	// when_stream_is_softdeleted_with_mixed_log_record_version_0_and_version_1
	//
	// not applicable:
	// when_metastream_is_scavenged_and_read_index_is_set_to_keep_last_3_metaevents


	[Fact]
	public async Task when_deleted_stream_with_a_lot_of_data_is_scavenged() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(9),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_deleted_stream_with_a_lot_of_data_is_scavenged_with_ingore_harddelete() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithUnsafeIgnoreHardDeletes(true)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepNone(),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_deleted_stream_with_explicit_transaction_is_scavenged() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithUnsafeIgnoreHardDeletes(true)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepNone(),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_deleted_stream_with_metadata_is_scavenged() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(10, null, null, null, null)),
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(2, null, null, null, null)),
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_having_nothing_to_scavenge() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"))
				.Chunk(
					Rec.Write(t++, "bla3"),
					Rec.Write(t++, "bla3"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepAll(),
				x.Recs[1].KeepAll(),
				x.Recs[2].KeepAll(),
			});
	}

	[Fact]
	public async Task when_having_stream_with_both_max_age_and_max_count_with_stricter_max_age_specified() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(5, TimeSpan.FromMinutes(5), null, null, null)),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(100)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(90)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(60)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(40)),

					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(30)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(20)),

					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(3)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(2)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(1)))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++, timeStamp: DateTime.UtcNow)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(0, 8, 9, 10),
					x.Recs[1].KeepAll(),
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(0, 6, 7, 8, 9, 10),
						x.Recs[1],
					});
	}

	[Fact]
	public async Task when_having_stream_with_both_max_age_and_max_count_with_stricter_max_count_specified() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(3, TimeSpan.FromMinutes(50), null, null, null)),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(100)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(90)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(60)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(40)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(30)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(20)),

					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(3)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(2)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(1)))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++, timeStamp: DateTime.UtcNow)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 8, 9, 10),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_having_stream_with_max_age_specified() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(null, TimeSpan.FromMinutes(5), null, null, null)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(110)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(100)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(90)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(60)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(40)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(30)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(20)),

					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(3)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(2)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(1)))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++, timeStamp: DateTime.UtcNow)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(0, 8, 9, 10),
					x.Recs[1].KeepAll(),
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
						x.Recs[1],
					});
	}

	[Fact]
	public async Task when_having_stream_with_max_count_specified() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(maxCount: 3)),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),

					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 8, 9, 10),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_having_stream_with_truncatebefore_specified() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(truncateBefore: 7)),
					Rec.Write(t++, "bla"), // event 0
					Rec.Write(t++, "bla"), // event 1
					Rec.Write(t++, "bla"), // event 2
					Rec.Write(t++, "bla"), // event 3
					Rec.Write(t++, "bla"), // event 4
					Rec.Write(t++, "bla"), // event 5
					Rec.Write(t++, "bla"), // event 6
					Rec.Write(t++, "bla"), // event 7
					Rec.Write(t++, "bla"), // event 8
					Rec.Write(t++, "bla")) // event 9
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 8, 9, 10),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_having_stream_with_strict_max_age_leaving_no_events_in_stream() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(null, TimeSpan.FromMinutes(1), null, null, null)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(25)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(20)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(10)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(5)),
					Rec.Write(t++, "bla", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(3)))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++, timeStamp: DateTime.UtcNow)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(0, 5),
					x.Recs[1].KeepAll(),
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(0, 1, 2, 3, 4, 5),
						x.Recs[1],
					});
	}

	[Fact]
	public async Task when_metastream_is_scavenged_and_read_index_is_set_to_keep_just_last_metaevent() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(10, null, null, null, null)),
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(5, null, null, null, null)),
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(3, null, null, null, null)),
					Rec.Write(t++, "$$bla", metadata: new StreamMetadata(2, null, null, null, null)))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(3),
				x.Recs[1].KeepAll(),
			});
	}

	[Fact]
	public async Task when_stream_is_deleted() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"))
				.Chunk(
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepNone(),
				x.Recs[1].KeepAll(),
				x.Recs[2].KeepAll(),
			});
	}

	[Fact]
	public async Task when_stream_is_deleted_with_ignore_hard_deletes() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithUnsafeIgnoreHardDeletes(true)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "bla"),
					Rec.Write(t++, "bla"))
				.Chunk(
					Rec.CommittedDelete(t++, "bla"))
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepNone(),
				x.Recs[1].KeepNone(),
				x.Recs[2].KeepAll(),
			});
	}

	// old scavenge removes transaction 0 entirely because it is contained within one chunk
	// old scavenge keeps the begin and commit for transaction 1 because it spans chunks
	// new scavenge has less transaction handling and treats both transactions the same.
	[Fact]
	public async Task when_stream_is_deleted_and_bulk_transaction_spans_chunks_boundary() {
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Prepare(0, "bla"), // keep (old would discard)
					Rec.Commit(0, "bla"), // keep (old would discard)
					Rec.Prepare(1, "bla"), // keep
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"))
				.Chunk(
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Commit(1, "bla"), // keep
					Rec.Delete(2, "bla"), // keep
					Rec.Commit(2, "bla")) // keep
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(3)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(0, 1, 2),
					x.Recs[1].KeepIndexes(3, 4, 5),
					x.Recs[2].KeepAll(),
				},
				x => new[] {
					x.Recs[0].KeepIndexes(),
					x.Recs[1].KeepIndexes(4),
					x.Recs[2].KeepAll(),
				});
	}

	[Fact]
	public async Task when_stream_is_deleted_and_explicit_transaction_spans_chunks_boundary() {
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Prepare(0, "bla"), // keep (old would discard)
					Rec.Commit(0, "bla"), // keep (old would discard)
					Rec.TransSt(1, "bla"), // keep
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"))
				.Chunk(
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.Prepare(1, "bla"),
					Rec.TransEnd(1, "bla"),
					Rec.Commit(1, "bla")) // keep
				.Chunk(
					Rec.Delete(2, "bla"), // keep
					Rec.Commit(2, "bla")) // keep
				.CompleteLastChunk()
				.Chunk(ScavengePointRec(3)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(0, 1, 2),
					x.Recs[1].KeepIndexes(6),
					x.Recs[2].KeepIndexes(0, 1),
					x.Recs[3].KeepAll(),
				},
				x => new[] {
					x.Recs[0].KeepIndexes(),
					x.Recs[1].KeepIndexes(),
					x.Recs[2].KeepIndexes(0),
					x.Recs[3].KeepAll(),
				});
	}
}
