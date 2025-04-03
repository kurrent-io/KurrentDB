// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;
#pragma warning disable CS0162 // Unreachable code detected

namespace EventStore.Core.XUnit.Tests.Scavenge;

// these tests test that the right steps happen and the right results are obtained when scavenge is
// run on a database that has already been scavenged.
// a new scavenge point may need to be created, but not necessarily.
public class SubsequentScavengeTests : SqliteDbPerTest<SubsequentScavengeTests> {
	[Fact]
	public async Task can_create_first_scavenge_point() {
		// first scavenge creates the first scavenge point SP-1
		var newScavengePoint = new List<ScavengePoint>();
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk())
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelOnNewScavengePoint(newScavengePoint)
			.RunAsync();

		Assert.Equal(ScavengeResult.Stopped, logger.Result);
		Assert.Collection(
			newScavengePoint,
			sp => {
				Assert.Equal(EffectiveNow, sp.EffectiveNow);
				Assert.Equal(0, sp.EventNumber);
				Assert.Equal(0, sp.Threshold);
			});
	}

	[Fact]
	public async Task can_create_subsequent_scavenge_point() {
		// set up some state and some chunks simulating a scavenge that has been completed
		// and then some new records added. it should create a new SP
		var newScavengePoint = new List<ScavengePoint>();
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					// events 0-4 removed in a previous scavenge
					Rec.Write(t++, "ab-1", eventNumber: 5))
				.Chunk(
					ScavengePointRec(t++))
				.Chunk(
					// two new records written since the previous scavenge
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk())
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.MutateState(x => {
				x.SetOriginalStreamMetadata("ab-1", MaxCount1);
				x.SetOriginalStreamDiscardPoints(
					StreamHandle.ForHash<string>(98),
					CalculationStatus.Active,
					DiscardPoint.DiscardBefore(5),
					DiscardPoint.DiscardBefore(5));
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 1,
					eventNumber: 0)));
			})
			.CancelOnNewScavengePoint(newScavengePoint)
			.RunAsync();

		Assert.Equal(ScavengeResult.Stopped, logger.Result);
		Assert.Collection(
			newScavengePoint,
			sp => {
				Assert.Equal(EffectiveNow, sp.EffectiveNow);
				Assert.Equal(1, sp.EventNumber);
				Assert.Equal(0, sp.Threshold);
			});
	}

	[Fact]
	public async Task can_find_existing_scavenge_point() {
		// set up some state and some chunks simulating a scavenge that has been completed
		// and then some new records added including a SP. it should perform an an incremental
		// scavenge using that SP.
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					// events 0-4 removed in a previous scavenge
					Rec.Write(t++, "ab-1", eventNumber: 5))
				.Chunk(
					ScavengePointRec(t++)) // <-- SP-0 scavenged previously
				.Chunk(
					// five new records written since the previous scavenge
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					ScavengePointRec(t++)) // <-- SP-1 added by another node
				.Chunk(
					Rec.Write(t++, "ab-1"),
					ScavengePointRec(t++)) // <-- SP-2 added by another node
				.Chunk(
					Rec.Write(t++, "ab-1"))
				.CompleteLastChunk())
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				x.SetOriginalStreamMetadata("ab-1", MaxCount1);
				x.SetOriginalStreamDiscardPoints(
					StreamHandle.ForHash<string>(98),
					CalculationStatus.Active,
					DiscardPoint.DiscardBefore(5),
					DiscardPoint.DiscardBefore(5));
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 1,
					eventNumber: 0)));
				for (int i = 0; i < 6; i++)
					x.SetChunkTimeStampRange(i, new ChunkTimeStampRange(DateTime.UtcNow, DateTime.UtcNow));
			})
			.AssertTrace(
				Tracer.Line("Accumulating from SP-0 to SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-2 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 2"),
				Tracer.Line("        Checkpoint: Accumulating SP-2 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 3"),
				Tracer.Line("        Checkpoint: Accumulating SP-2 done Chunk 3"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 4"),
				Tracer.Line("        Checkpoint: Accumulating SP-2 done Chunk 4"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-2 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 9, Discard before 9)"),
				Tracer.Line("        Checkpoint: Calculating SP-2 done Hash: 98"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("    Switched in chunk-000000.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 2-2"),
				Tracer.Line("    Opening Chunk 2-2"),
				Tracer.Line("    Switched in chunk-000002.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 3-3"),
				Tracer.Line("    Opening Chunk 3-3"),
				Tracer.Line("    Switched in chunk-000003.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done Chunk 3"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 4-4"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-2 done Chunk 4"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-2"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-2"),
				Tracer.Line("Commit"))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1].KeepIndexes(0),
				x.Recs[2].KeepIndexes(),
				x.Recs[3].KeepIndexes(1),
				x.Recs[4].KeepIndexes(0, 1),
				x.Recs[5].KeepIndexes(0),
			});
	}

	// syncOnly prevents a new scavenge point from being created, it only scavenges an existing one.
	// so we need to check
	// - that it scavenges as normal if there is one to do
	// - that it stops successfully if there isn't one at all
	// - that it stops successfully if it has done all of them.
	[Fact]
	public async Task can_sync_only_with_scavenge_point_to_do() {
		// first scavenge creates the first scavenge point SP-1
		var newScavengePoint = new List<ScavengePoint>();
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++))) // SP-0
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithSyncOnly(true)
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 2),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task can_sync_only_with_no_scavenge_point() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1")))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithSyncOnly(true)
			.AssertTrace()
			.RunAsync(x => new[] {
				x.Recs[0], // not scavenged
			});
	}

	[Fact]
	public async Task can_sync_only_with_completed_scavenge_point() {
		var newScavengePoint = new List<ScavengePoint>();
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(ScavengePointRec(t++)) // SP-0
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1")))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 0,
					eventNumber: 0)));
			})
			.WithSyncOnly(true)
			.AssertTrace()
			.RunAsync(x => new[] {
				x.Recs[0],
				x.Recs[1], // not scavenged
			});
	}

	[Fact]
	public async Task can_subsequent_scavenge_without_state() {
		// say we deleted the state, or old scavenge has been run but not new scavenge
		// so there is no state.
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					// events 0-4 removed in a previous scavenge
					Rec.Write(t++, "ab-1", eventNumber: 5))
				.Chunk(
					ScavengePointRec(t++)) // <-- SP-0 previous scavenge
				.Chunk(
					// five new records written since the previous scavenge
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					ScavengePointRec(t++)) // <-- SP-1 added by another node
				.Chunk(
					Rec.Write(t++, "ab-1"),
					ScavengePointRec(t++)) // <-- SP-2 added by another node
				.Chunk(
					Rec.Write(t++, "ab-1"))
				.CompleteLastChunk())
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1].KeepIndexes(0),
				x.Recs[2].KeepIndexes(),
				x.Recs[3].KeepIndexes(1),
				x.Recs[4].KeepIndexes(0, 1),
				x.Recs[5].KeepIndexes(0),
			});
	}

	[Fact]
	public async Task accumulates_from_right_place_sp_in_chunk_0() {
		// set up as if we have done a scavenge with a SP in chunk 0
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(ScavengePointRec(t++)) // SP-0
				.Chunk(ScavengePointRec(t++, threshold: 1))) // SP-1
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 0,
					eventNumber: 0)));
			})
			.AssertTrace(
				Tracer.Line("Accumulating from SP-0 to SP-1"),
				// the important bit: we start accumulation from the chunk after the SP is in
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-1 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-1 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-1 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-1 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-1 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-1"),
				Tracer.Line("Commit"))
			.RunAsync(x => new[] {
				x.Recs[0],
				x.Recs[1],
			});
	}

	[Fact]
	public async Task accumulates_from_right_place_sp_in_chunk_2() {
		// set up as if we have done a scavenge with a SP in chunk 2
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(ScavengePointRec(t++)) // SP-0
				.Chunk(ScavengePointRec(t++)) // SP-1
				.Chunk(ScavengePointRec(t++)) // SP-2
				.Chunk(ScavengePointRec(t++, threshold: 1))) // SP-3
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 2,
					eventNumber: 2)));
			})
			.AssertTrace(
				Tracer.Line("Accumulating from SP-2 to SP-3"),
				// the important bit: we start accumulation from the chunk after the prev SP is in
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-3 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 3"),
				Tracer.Line("        Checkpoint: Accumulating SP-3 done Chunk 3"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-3"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-3 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-3"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-3 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-3 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-3 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 2-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-3 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-3"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-3"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-3"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-3"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-3"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-3"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-3"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-3"),
				Tracer.Line("Commit"))
			.RunAsync(x => new[] {
				x.Recs[0],
				x.Recs[1],
				x.Recs[2],
				x.Recs[3],
			});
	}

	[Fact]
	public async Task cannot_move_discard_points_backward() {
		// scavenge where SP-0 has been run. about to run SP-1
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"), // 0
					Rec.Write(t++, "ab-1"), // 1
					Rec.Write(t++, "ab-1")) // 2
				.Chunk(
					ScavengePointRec(t++)) // <-- SP-0
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount4),
					Rec.Write(t++, "ab-1"), // 3
					Rec.Write(t++, "ab-1")) // 4
				.Chunk(
					ScavengePointRec(t++))) // <-- SP-1
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.MutateState(x => {
				x.DetectCollisions("$$ab-1");
				x.DetectCollisions("ab-1");
				x.SetOriginalStreamMetadata("ab-1", MaxCount1);
				x.SetOriginalStreamDiscardPoints(
					Scenario.CollideEverything
						? StreamHandle.ForStreamId("ab-1")
						: StreamHandle.ForHash<string>(98),
					CalculationStatus.Active,
					DiscardPoint.DiscardBefore(2),
					DiscardPoint.DiscardBefore(2));
				x.SetCheckpoint(new ScavengeCheckpoint.Done(ScavengePoint(
					chunk: 1,
					eventNumber: 0)));

				for (int i = 0; i < 4; i++)
					x.SetChunkTimeStampRange(i, new ChunkTimeStampRange(DateTime.UtcNow, DateTime.UtcNow));
			})
			.AssertState(state => {
				// we changed the maxcount to 4, but we expect the discard points to remain
				// where they are rather than moving back to DiscardBefore(1)
				Assert.True(state.TryGetOriginalStreamData("ab-1", out var data));
				Assert.Equal(DiscardPoint.DiscardBefore(2), data.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(2), data.MaybeDiscardPoint);
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(3),
				x.Recs[1],
				x.Recs[2],
				x.Recs[3],
			});
	}

	[Fact]
	public async Task stream_starts_after_scavenge_point() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: TruncateBefore4),
					ScavengePointRec(t++))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1")))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0],
				x.Recs[1],
			});
	}
}
