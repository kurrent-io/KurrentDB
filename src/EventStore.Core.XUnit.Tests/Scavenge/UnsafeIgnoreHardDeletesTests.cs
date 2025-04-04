// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class UnsafeIgnoreHardDeletesTests : SqliteDbPerTest<UnsafeIgnoreHardDeletesTests> {
	[Fact]
	public async Task simple_tombstone() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithUnsafeIgnoreHardDeletes()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.False(state.TryGetOriginalStreamData("ab-1", out _));
				Assert.False(state.TryGetMetastreamData("$$ab-1", out _));
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task tombstone_then_normal_scavenge_then_unsafeharddeletes() {
		// after the normal scavenge of a tombstoned stream we still need to be
		// able to remove the tombstone with a unsafeharddeletes scavenge

		// the normal scavenge with the tombstone
		var t = 0;
		var scenario = new Scenario<LogFormat.V2, string>();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)) // SP-0
				.Chunk(ScavengePointRec(t++))) // SP-1
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				// make it scavenge SP-0
				x.SetCheckpoint(new ScavengeCheckpoint.Accumulating(
					ScavengePoint(
						chunk: 1,
						eventNumber: 0),
					doneLogicalChunkNumber: null));
			})
			.RunAndKeepDbAsync(x => new[] {
				x.Recs[0].KeepIndexes(3), // only the tombstone is kept
				x.Recs[1],
				x.Recs[2],
			});

		// the second scavenge with unsafeharddeletes should remove the tombstone
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithUnsafeIgnoreHardDeletes()
			.AssertState(state => {
				Assert.False(state.TryGetOriginalStreamData("ab-1", out _));
				Assert.False(state.TryGetMetastreamData("$$ab-1", out _));
			})
			.RunAsync(x => new[] {
					x.Recs[0].KeepIndexes(), // tombstone has gone
					x.Recs[1],
					x.Recs[2],
				});
	}

	[Fact]
	public async Task normal_scavenge_then_tombstone_then_unsafeharddeletes() {
		// after the normal scavenge runs and clears the metastreamdatas,
		// we need to be able to tombstone a stream and still be able to remove
		// the leftover metadata record.

		// the normal scavenge with the tombstone
		var t = 0;
		var scenario = new Scenario<LogFormat.V2, string>();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)) // SP-0
				.Chunk(Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++))) // SP-1
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				// make it scavenge SP-0
				x.SetCheckpoint(new ScavengeCheckpoint.Accumulating(
					ScavengePoint(
						chunk: 1,
						eventNumber: 0),
					doneLogicalChunkNumber: null));
			})
			.AssertTrace(
				Tracer.Line("Accumulating from checkpoint: Accumulating SP-0 done None"),
				Tracer.AnythingElse)
			// result of scavenging SP-0
			.RunAndKeepDbAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 2),
				x.Recs[1],
				x.Recs[2],
				x.Recs[3],
			});

		// the second scavenge with unsafeharddeletes
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithUnsafeIgnoreHardDeletes()
			.AssertTrace(
				Tracer.Line("Accumulating from SP-0 to SP-1"),
				Tracer.AnythingElse)
			.AssertState(state => {
				Assert.False(state.TryGetOriginalStreamData("ab-1", out _));
				Assert.False(state.TryGetMetastreamData("$$ab-1", out _));
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(), // metadata record has gone
				x.Recs[1],
				x.Recs[2].KeepIndexes(), // tombstone has gone
				x.Recs[3],
			});
	}
}
