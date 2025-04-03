// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class CancellationAndContinuationTests : SqliteDbPerTest<CancellationAndContinuationTests> {
	// in these tests we we want to
	// - run a scavenge
	// - have a log record trigger the cancellation of that scavenge at a particular point
	// - check that the checkpoint has been set correctly in the scavenge state
	// - complete the scavenge
	// - check it continued from the checkpoint
	// - check it produced the right results

	[Fact]
	public async Task accumulator_checkpoints_immediately() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$cd-cancel-accumulation"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenAccumulatingMetaRecordFor("cd-cancel-accumulation")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("    Rollback"),
				Tracer.Line("Exception accumulating"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var accumulating = Assert.IsType<ScavengeCheckpoint.Accumulating>(checkpoint);
				Assert.Null(accumulating.DoneLogicalChunkNumber);
			})
			.RunAsync();
	}

	[Fact]
	public async Task calculator_checkpoints_immediately() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "cd-cancel-calculation"),
					Rec.Write(t++, "$$cd-cancel-calculation", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenCalculatingOriginalStream("cd-cancel-calculation")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("    Rollback"),
				Tracer.Line("Exception calculating"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var calculating = Assert.IsType<ScavengeCheckpoint.Calculating<string>>(checkpoint);
				Assert.Equal("None", calculating.DoneStreamHandle.ToString());
			})
			.RunAsync();
	}

	[Fact]
	public async Task chunk_executor_checkpoints_immediately() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "cd-cancel-chunk-execution"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 1, Discard before 1)"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done Hash: 98"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("Exception executing chunks"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
				Assert.Null(executing.DoneLogicalChunkNumber);
			})
			.RunAsync();
	}

	[Fact]
	public async Task index_executor_checkpoints_immediately() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "cd-cancel-index-execution"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenExecutingIndexEntry("cd-cancel-index-execution")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Exception executing index"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.ExecutingIndex>(checkpoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task cleaner_checkpoints_immediately() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenCheckpointing<ScavengeCheckpoint.Cleaning>()
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Exception cleaning"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.Cleaning>(checkpoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task can_cancel_during_accumulation_and_resume() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		var scenario = new Scenario<LogFormat.V2, string>();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					Rec.Write(t++, "$$cd-cancel-accumulation"))
				.Chunk(
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenAccumulatingMetaRecordFor("cd-cancel-accumulation")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("    Rollback"),
				Tracer.Line("Exception accumulating"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var accumulating = Assert.IsType<ScavengeCheckpoint.Accumulating>(checkpoint);
				Assert.Equal(0, accumulating.DoneLogicalChunkNumber);
			})
			.RunAndKeepDbAsync();

		// now complete the scavenge
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertTrace(
				// accumulation continues from checkpoint
				Tracer.Line("Accumulating from checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 2"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 3"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 3"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				// the rest is fresh for SP-0
				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 1, Discard before 1)"),
				Tracer.Line("        SetDiscardPoints(100, Spent, Keep all, Keep all)"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done Hash: 100"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("    Switched in chunk-000000.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 2-2"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				// scavenge completed
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
				Assert.Equal("SP-0", done.ScavengePoint.GetName());
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 2),
				x.Recs[1].KeepIndexes(0),
				x.Recs[2].KeepIndexes(0),
				x.Recs[3],
			});
	}

	[Fact]
	public async Task can_cancel_during_calculation_and_resume() {
		var t = 0;
		var scenario = new Scenario<LogFormat.V2, string>();
		var logger = new FakeTFScavengerLog();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-cancel-calculation"),
					Rec.Write(t++, "$$cd-cancel-calculation", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenCalculatingOriginalStream("cd-cancel-calculation")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 2"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 2, Discard before 2)"),
				// throw while calculating 100
				Tracer.Line("    Rollback"),
				Tracer.Line("Exception calculating"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var calculating = Assert.IsType<ScavengeCheckpoint.Calculating<string>>(checkpoint);
				Assert.Equal("None", calculating.DoneStreamHandle.ToString());
			})
			.RunAndKeepDbAsync();

		// now complete the scavenge
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertTrace(
				// no accumulation
				// calculation continues from checkpoint
				Tracer.Line("Calculating from checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 2, Discard before 2)"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done Hash: 100"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				// the rest is fresh for SP-0
				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("    Switched in chunk-000000.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				// scavenge completed
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1].KeepIndexes(0, 1, 2),
				x.Recs[2],
			});
	}

	[Fact]
	public async Task can_cancel_during_chunk_execution_and_resume() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		var scenario = new Scenario<LogFormat.V2, string>();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-2", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "cd-cancel-chunk-execution"),
					Rec.Write(t++, "ab-2"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenExecutingChunk("cd-cancel-chunk-execution")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 2"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(ab-1, Active, Discard before 1, Discard before 1)"),
				// no discard points to set for ab-2
				Tracer.Line("        Checkpoint: Calculating SP-0 done Id: ab-2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Opening Chunk 1-1"),
				Tracer.Line("Exception executing chunks"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(checkpoint);
				Assert.Equal(0, executing.DoneLogicalChunkNumber);
			})
			.RunAndKeepDbAsync();

		// now complete the scavenge
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertTrace(
				// no accumulation
				// no calculation
				// chunk execution continues from checkpoint
				Tracer.Line("Executing chunks from checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Opening Chunk 1-1"),
				Tracer.Line("    Switched in chunk-000001.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				// the rest is fresh for SP-0
				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				// scavenge completed
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1].KeepIndexes(1, 2, 3, 4),
				x.Recs[2],
			});
	}

	[Fact]
	public async Task can_cancel_during_index_execution_and_resume() {
		var t = 0;
		var logger = new FakeTFScavengerLog();
		var scenario = new Scenario<LogFormat.V2, string>();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-cancel-index-execution"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenExecutingIndexEntry("cd-cancel-index-execution")
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 2"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 2"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 3, Discard before 3)"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done Hash: 98"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("    Switched in chunk-000000.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 1-1"),
				Tracer.Line("    Opening Chunk 1-1"),
				Tracer.Line("    Switched in chunk-000001.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Exception executing index"))
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.ExecutingIndex>(checkpoint);
			})
			.RunAndKeepDbAsync();

		// now complete the scavenge
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			// makes sure we dont reaccumulate
			.CancelWhenAccumulatingMetaRecordFor("ab-1")
			// make sure we dont recalculate
			.CancelWhenCalculatingOriginalStream("ab-1")
			// make sure we dont rescavenge the chunks
			.CancelWhenExecutingChunk("ab-1")
			.AssertTrace(
				Tracer.Line("Executing index from checkpoint: Executing index for SP-0"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				// scavenge completed
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1].KeepIndexes(1, 2),
				x.Recs[2],
			});
	}

	[Fact]
	public async Task can_cancel_during_cleaning_and_resume() {
		var t = 0;
		var scenario = new Scenario<LogFormat.V2, string>();
		var logger = new FakeTFScavengerLog();
		var db = await scenario
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: TruncateBefore1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.CancelWhenCheckpointing<ScavengeCheckpoint.Cleaning>()
			.AssertState(state => {
				Assert.Equal(ScavengeResult.Stopped, logger.Result);
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.Cleaning>(checkpoint);
			})
			.RunAndKeepDbAsync();

		// now complete the scavenge
		await new Scenario<LogFormat.V2, string>()
			.WithTracerFrom(scenario)
			.WithDbPath(Fixture.Directory)
			.WithDb(db)
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertTrace(
				Tracer.Line("Cleaning from checkpoint Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				// scavenge completed
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var done = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);

				Assert.False(state.TryGetOriginalStreamData("ab-1", out _));
				Assert.False(state.TryGetMetastreamData("$$ab-1", out _));
			})
			.RunAsync();
	}

	[Fact]
	public async Task can_complete() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertTrace(
				Tracer.Line("Accumulating from start to SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 0"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Reading Chunk 1"),
				Tracer.Line("        Checkpoint: Accumulating SP-0 done Chunk 1"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Calculating SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        SetDiscardPoints(98, Active, Discard before 1, Discard before 1)"),
				Tracer.Line("        Checkpoint: Calculating SP-0 done Hash: 98"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done None"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Retaining Chunk 0-0"),
				Tracer.Line("    Opening Chunk 0-0"),
				Tracer.Line("    Switched in chunk-000000.000001"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing chunks for SP-0 done Chunk 0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Merging chunks for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Merging chunks for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Executing index for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Executing index for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Cleaning for SP-0"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("    Begin"),
				Tracer.Line("        Checkpoint: Cleaning for SP-0"),
				Tracer.Line("    Commit"),
				Tracer.Line("Done"),

				Tracer.Line("Begin"),
				Tracer.Line("    Checkpoint: Done SP-0"),
				Tracer.Line("Commit"))
			.AssertState(state => {
				Assert.True(state.TryGetCheckpoint(out var checkpoint));
				var executing = Assert.IsType<ScavengeCheckpoint.Done>(checkpoint);
			})
			.RunAsync();
	}
}
