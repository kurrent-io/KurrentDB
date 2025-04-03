// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

// for testing functionality that isn't specific to particular discard criteria
public class MiscellaneousTests : SqliteDbPerTest<MiscellaneousTests> {
	[Fact]
	public async Task Trivial() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					// the first letter of the stream name determines its hash value
					// ab-1: a stream called "ab-1" which hashes to "b"
					Rec.Write(t++, "ab-1"),

					// setting metadata for ab-1, which hashes to "a" and does not collide with "b"
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task seen_stream_before() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task collision() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-2"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadata_non_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadata_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "aa-1"),
					Rec.Write(t++, "$$aa-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadata_applies_to_correct_stream_in_hidden_collision() {
		// metastream sets metadata for stream ab-1 (which hashes to b)
		// but that stream doesn't exist.
		// make sure that cb-2 (which also hashes to b) does not pick up that metadata.
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "cb-2"),
					Rec.Write(t++, "cb-2"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1, 2),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_non_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$cd-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$cd-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_all_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$aa-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$aa-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$aa-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$aa-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_original_streams_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$cb-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$cb-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_meta_streams_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$ac-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$ac-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_original_and_meta_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$ab-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$ab-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_different_streams_cross_colliding() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$ba-2", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$ba-2", "$metadata", metadata: MaxCount4))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadatas_for_same_stream() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task metadata_first() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 4),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task nonexistent_stream() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task multiple_streams() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-2"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-2"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$cd-2", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(2, 3, 4, 5),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task metadata_gets_scavenged() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task metadata_for_metadata_stream_gets_scavenged() {
		// currently requires a threshold = -1 scavenge to force this.
		// see comments in accumulator.cs
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "$$$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++, threshold: -1)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task metadata_for_metadata_stream_does_not_apply() {
		// e.g. can't increase the maxcount to three
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$$$ab-1", "$metadata", metadata: MaxCount3),
					Rec.Write(t++, "$$ab-1"),
					Rec.Write(t++, "$$ab-1"),
					Rec.Write(t++, "$$ab-1"),
					Rec.Write(t++, "$$ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(0, 4),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task unusual_metadata_still_takes_effect() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					// this 'metadata' record has a strange type, and does not parse to metadata
					// still, its effect is to reset the metadata of the stream.
					Rec.Write(
						transaction: t++,
						stream: "$$ab-1",
						eventType: "sneaky",
						data: BitConverter.GetBytes(0xBAD_1DEA)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(1, 2, 3),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task metadata_in_normal_stream_is_ignored() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0],
					x.Recs[1],
				});
	}

	[Fact]
	public async Task metadata_in_transaction_not_supported() {
		var logger = new FakeTFScavengerLog();

		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.TransSt(0, "$$ab-1"),
					Rec.Prepare(0, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(1)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.RunAsync();

		Assert.True(logger.Completed);
		Assert.Equal(EventStore.Core.TransactionLog.Chunks.ScavengeResult.Errored, logger.Result);
		Assert.Equal("Error while scavenging DB: Found metadata in transaction in stream $$ab-1.", logger.Error);
	}

	[Fact]
	public async Task transactions_not_scavenged() {
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(0, "ab-1"),
					Rec.TransSt(1, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.TransEnd(1, "ab-1"),
					Rec.Commit(1, "ab-1"),
					Rec.Write(2, "ab-1"),
					Rec.Write(3, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(4)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(1, 2, 3, 4, 5, 6),
					x.Recs[1],
				},
				// still removed from the index
				x => new[] {
					x.Recs[0].KeepIndexes(5, 6),
					x.Recs[1],
				});
	}
}
