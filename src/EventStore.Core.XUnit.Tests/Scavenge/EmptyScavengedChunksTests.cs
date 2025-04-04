// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

// the old scavenger can leave behind empty, scavenged chunks when
// all events from a chunk are deleted. if the scavenger is interrupted
// before the index scavenge phase, entries pointing to these
// empty chunks may be present in the index.
//
// note: it may also happen that the merge phase has completed and the empty chunks
// have been merged but these will be similar to the before-merge case as the
// new scavenger works on logical chunk numbers.

public class EmptyScavengedChunksTests : SqliteDbPerTest<MaxAgeTests> {
	// let's assume we have an index entry X pointing to an empty, scavenged chunk.
	// call the stream for that index entry: S.
	//
	// there are the following cases:
	//
	// Case A. no scavenge data (metadata, tombstone) for S exists in the log:
	// as usual:
	// - no events of S will be deleted
	// - X will stay in the index
	//
	// Case B. scavenge data (metadata, tombstone) for S exists in the log:
	// - as usual, scavenge data will be accumulated and discard points calculated
	// - X must or must not be deleted from the index, depending on the cases below:
	//
	// B.1: X is the first index entry for the stream
	// delete X since all events prior to X are deleted and X doesn't exist in the log
	//
	// B.2: X is not the first index entry for the stream and the previous index entry is not another "X"
	//
	// B.2.1: based on scavenge data, the index entry just before X will be discarded for sure (i.e not 'maybe' discardable)
	// delete X since all events prior to X will be deleted and X doesn't exist in the log
	//
	// B.2.2: based on scavenge data, the index entry just before X is 'maybe' discardable or will be kept for sure
	// keep X since not all events prior to X may be deleted from the log
	//
	// B.3: X is not the first index entry for the stream and the previous index entry is another "X": call it X'
	//
	// B.3.1: X' has/will be discarded from the index (due to rules B.1 or B.2.1)
	// delete X since all events prior to X are/will be deleted and X doesn't exist in the log
	//
	// B.3.2: X' will not be discarded from the index (due to rule B.2.2)
	// keep X since not all events prior to X may be deleted from the log
	//
	// assumptions:
	// i)  'discardable for sure' events cannot occur after 'maybe discardable' events
	// ii) 'discardable for sure' events cannot occur after 'kept for sure' events
	// iii) 'maybe discardable' events cannot occur after 'kept for sure' events

	[Fact]
	public async Task case_a() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.EmptyChunk(0)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(x => {
				Assert.False(x.TryGetOriginalStreamData("ab-1", out _)); // no scavenge data accumulated
			})
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepNone(), // emptied prior to scavenge
					x.Recs[1].KeepAll()
				},
				x => new[] {
					x.Recs[0].KeepAll(),
					x.Recs[1].KeepAll()
				}
			);
	}

	[Fact]
	public async Task case_b() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.1: delete
					Rec.Write(t++, "ab-1")) // B.3.1: delete
				.Chunk(
					Rec.Write(t++, "ab-1"), // delete (before truncate before)
					Rec.Write(t++, "ab-1")) // delete (before truncate before)
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.2.1: delete
					Rec.Write(t++, "ab-1")) // B.3.1: delete <-- discard point must move here at event 5
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Cutoff - TimeSpan.FromSeconds(1)), // maybe deleted
					Rec.Write(t++, "ab-1", timestamp: Cutoff),                           // maybe deleted
					Rec.Write(t++, "ab-1", timestamp: Cutoff + TimeSpan.FromSeconds(1))) // maybe deleted <-- maybe discard point must move here at event 8
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.2.2: keep
					Rec.Write(t++, "ab-1")) // B.3.2: keep
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Active), // keep (not expired)
					Rec.Write(t++, "ab-1", timestamp: Active), // keep (not expired)
					Rec.Write(t++, "ab-1", timestamp: Active)) // keep (not expired)
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.2.2: keep
					Rec.Write(t++, "ab-1")) // B.3.2: keep
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Active), // added just to create a valid last event that's readable from the log
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxAge: MaxAgeTimeSpan,
						truncateBefore: 4
					)))
				.Chunk(ScavengePointRec(t++)))
			.EmptyChunk(0)
			.EmptyChunk(2)
			.EmptyChunk(4)
			.EmptyChunk(6)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(x => {
				if (!x.TryGetOriginalStreamData("ab-1", out var data))
					Assert.Fail("Failed to get original stream data");

				Assert.Equal(DiscardPoint.DiscardIncluding(5), data.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardIncluding(8), data.MaybeDiscardPoint);
			})
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepNone(), // emptied prior to scavenge
					x.Recs[1].KeepNone(),
					x.Recs[2].KeepNone(), // emptied prior to scavenge
					x.Recs[3].KeepIndexes(1, 2),
					x.Recs[4].KeepNone(), // emptied prior to scavenge
					x.Recs[5].KeepAll(),
					x.Recs[6].KeepNone(), // emptied prior to scavenge
					x.Recs[7].KeepAll(),
					x.Recs[8].KeepAll()
				},
				x => new[] {
					x.Recs[0].KeepNone(),
					x.Recs[1].KeepNone(),
					x.Recs[2].KeepNone(),
					x.Recs[3].KeepAll(), // all kept since 'maybe' discardable
					x.Recs[4].KeepAll(),
					x.Recs[5].KeepAll(),
					x.Recs[6].KeepAll(),
					x.Recs[7].KeepAll(),
					x.Recs[8].KeepAll()
				}
			);
	}

	[Fact]
	public async Task case_b_with_no_events_in_the_log() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.1: delete
					Rec.Write(t++, "ab-1"), // B.3.1: delete <-- discard point must move here
					Rec.Write(t++, "ab-1")) // B.3.1: normally deleted but kept due to overriding rule to keep last index entry
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
				.Chunk(ScavengePointRec(t++)))
			.EmptyChunk(0)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(x => {
				if (!x.TryGetOriginalStreamData("ab-1", out var data))
					Assert.Fail("Failed to get original stream data");

				Assert.Equal(DiscardPoint.DiscardIncluding(1), data.DiscardPoint);
			})
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepNone(), // emptied prior to scavenge
					x.Recs[1].KeepAll(),
					x.Recs[2].KeepAll()
				},
				x => new[] {
					x.Recs[0].KeepIndexes(2),
					x.Recs[1].KeepAll(),
					x.Recs[2].KeepAll()
				}
			);
	}

	[Fact]
	public async Task case_b_with_modified_tb_metadata() {
		// in a real-life scenario, $tb would normally be 4. but in this test, we change it to 2 to illustrate that
		// the $tb metadata will take precedence. Given that this stream doesn't have maxage metadata, for performance
		// reasons we don't consult the chunk's time stamp range and thus won't know if these index entries have already
		// been discarded.

		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"), // delete (before truncate before)
					Rec.Write(t++, "ab-1")) // delete (before truncate before) <-- discard point must move here at event 1
				.Chunk(
					Rec.Write(t++, "ab-1"), // B.2.1: normally deleted, but kept since truncate before takes precedence where there is no maxage metadata
					Rec.Write(t++, "ab-1")) // B.3.1: normally deleted, but kept since truncate before takes precedence where there is no maxage metadata
				.Chunk(
					Rec.Write(t++, "ab-1"), // keep (after truncate before)
					Rec.Write(t++, "ab-1"), // keep (after truncate before)
					Rec.Write(t++, "ab-1")) // keep (after truncate before)
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: TruncateBefore2))
				.Chunk(ScavengePointRec(t++)))
			.EmptyChunk(1)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(x => {
				Assert.False(x.TryGetOriginalStreamData("ab-1", out _)); // calculation status is 'spent'
			})
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepNone(),
					x.Recs[1].KeepNone(), // emptied prior to scavenge
					x.Recs[2].KeepAll(),
					x.Recs[3].KeepAll(),
					x.Recs[4].KeepAll()
				},
				x => new[] {
					x.Recs[0].KeepNone(),
					x.Recs[1].KeepAll(), // all index entries kept
					x.Recs[2].KeepAll(),
					x.Recs[3].KeepAll(),
					x.Recs[4].KeepAll()
				}
			);
	}

	[Fact]
	public async Task case_b_with_modified_maxage_metadata() {
		// in a real-life scenario, the timestamp of events in the emptied chunk would normally be 'Expired'.
		// but in this test, we change it to 'Active' to illustrate that the stale index entries will still be deleted.

		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // delete (expired)
					Rec.Write(t++, "ab-1", timestamp: Expired)) // delete (expired)
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Active), // B.2.1: delete, although original event wasn't expired
					Rec.Write(t++, "ab-1", timestamp: Active)) // B.3.1: delete, although original event wasn't expired <-- discard point must move here at event 3
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Active), // keep (not expired)
					Rec.Write(t++, "ab-1", timestamp: Active), // keep (not expired)
					Rec.Write(t++, "ab-1", timestamp: Active)) // keep (not expired)
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
				.Chunk(ScavengePointRec(t++)))
			.EmptyChunk(1)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(x => {
				if (!x.TryGetOriginalStreamData("ab-1", out var data))
					Assert.Fail("Failed to get original stream data");

				Assert.Equal(DiscardPoint.DiscardIncluding(3), data.DiscardPoint);
			})
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepNone(),
					x.Recs[1].KeepNone(), // emptied prior to scavenge
					x.Recs[2].KeepAll(),
					x.Recs[3].KeepAll(),
					x.Recs[4].KeepAll()
				},
				x => new[] {
					x.Recs[0].KeepNone(),
					x.Recs[1].KeepNone(), // all index entries deleted
					x.Recs[2].KeepAll(),
					x.Recs[3].KeepAll(),
					x.Recs[4].KeepAll()
				}
			);
	}
}
