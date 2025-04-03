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

public class LogDisorderingTests : SqliteDbPerTest<LogDisorderingTests> {
	// if a metadata was ever written with the wrong event number (e.g. 0) due to old bugs
	// the rest of the system will not respect it, so scavenge must not either
	[Fact]
	public async Task wrong_order_metadata_does_not_apply() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1"))
				.Chunk(Rec.Write(t++, "ab-1"))
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 5, metadata: MaxCount2))
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0],
					x.Recs[1],
					x.Recs[2],
					x.Recs[3].KeepNone(),
					x.Recs[4],
				});
	}

	[Fact]
	public async Task wrong_order_metadata_does_not_apply_a() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1")) // 0
				.Chunk(Rec.Write(t++, "ab-1")) // 1
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount2)) // 2 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount1)) // 3 skip
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount1)) // 4 skip
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(0, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.Equal(0, state.SumChunkWeights(2, 2));
				Assert.Equal(2, state.SumChunkWeights(3, 3));
				Assert.Equal(2, state.SumChunkWeights(4, 4));

				Assert.True(state.TryGetOriginalStreamData("ab-1", out var originalStreamData));
				// not present because there is nothing to discard
				Assert.False(state.TryGetMetastreamData("$$ab-1", out var metastreamData));
		
				Assert.Equal(DiscardPoint.KeepAll, originalStreamData.DiscardPoint);
				Assert.Equal(DiscardPoint.KeepAll, metastreamData.DiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task wrong_order_metadata_does_not_apply_b() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1")) // 0
				.Chunk(Rec.Write(t++, "ab-1")) // 1
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 3, metadata: MaxCount1)) // 2 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 4, metadata: MaxCount2)) // 3 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 4, metadata: MaxCount1)) // 4 skip
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(0, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.Equal(2, state.SumChunkWeights(2, 2));
				Assert.Equal(0, state.SumChunkWeights(3, 3));
				Assert.Equal(2, state.SumChunkWeights(4, 4));

				Assert.True(state.TryGetOriginalStreamData("ab-1", out var originalStreamData));
				Assert.True(state.TryGetMetastreamData("$$ab-1", out var metastreamData));

				Assert.Equal(DiscardPoint.KeepAll, originalStreamData.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(4), metastreamData.DiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task wrong_order_metadata_does_not_apply_c() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1")) // 0
				.Chunk(Rec.Write(t++, "ab-1")) // 1
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 3, metadata: MaxCount1)) // 2 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 4, metadata: MaxCount2)) // 3 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount1)) // 4 skip
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(0, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.Equal(2, state.SumChunkWeights(2, 2));
				Assert.Equal(0, state.SumChunkWeights(3, 3));
				Assert.Equal(2, state.SumChunkWeights(4, 4));

				Assert.True(state.TryGetOriginalStreamData("ab-1", out var originalStreamData));
				Assert.True(state.TryGetMetastreamData("$$ab-1", out var metastreamData));

				Assert.Equal(DiscardPoint.KeepAll, originalStreamData.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(4), metastreamData.DiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task wrong_order_metadata_does_not_apply_d() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1")) // 0
				.Chunk(Rec.Write(t++, "ab-1")) // 1
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 4, metadata: MaxCount2)) // 2 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 2, metadata: MaxCount1)) // 3 skip
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount1)) // 4 skip
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(0, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.Equal(0, state.SumChunkWeights(2, 2));
				Assert.Equal(2, state.SumChunkWeights(3, 3));
				Assert.Equal(2, state.SumChunkWeights(4, 4));

				Assert.True(state.TryGetOriginalStreamData("ab-1", out var originalStreamData));
				Assert.True(state.TryGetMetastreamData("$$ab-1", out var metastreamData));

				Assert.Equal(DiscardPoint.KeepAll, originalStreamData.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(4), metastreamData.DiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task wrong_order_metadata_does_not_apply_e() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1")) // 0
				.Chunk(Rec.Write(t++, "ab-1")) // 1
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 4, metadata: MaxCount2)) // 2 apply
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 2, metadata: MaxCount1)) // 3 skip
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 3, metadata: MaxCount1)) // 4 skip
				.Chunk(ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(0, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.Equal(0, state.SumChunkWeights(2, 2));
				Assert.Equal(2, state.SumChunkWeights(3, 3));
				Assert.Equal(2, state.SumChunkWeights(4, 4));

				Assert.True(state.TryGetOriginalStreamData("ab-1", out var originalStreamData));
				Assert.True(state.TryGetMetastreamData("$$ab-1", out var metastreamData));

				Assert.Equal(DiscardPoint.KeepAll, originalStreamData.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(4), metastreamData.DiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task wrong_order_metadata_then_right_does_apply() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(Rec.Write(t++, "ab-1"))
				.Chunk(Rec.Write(t++, "ab-1"))
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 5, metadata: MaxCount1))
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 0, metadata: MaxCount3))
				.Chunk(Rec.Write(t++, "$$ab-1", "$metadata", eventNumber: 6, metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
					x.Recs[0],
					x.Recs[1],
					x.Recs[2].KeepNone(),
					x.Recs[3].KeepNone(),
					x.Recs[4],
					x.Recs[5],
				});
	}

	[Fact]
	public async Task wrong_order_in_original_stream_a() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", eventNumber: 0),
					Rec.Write(t++, "ab-1", eventNumber: 1),
					Rec.Write(t++, "ab-1", eventNumber: 2),
					Rec.Write(t++, "ab-1", eventNumber: 0),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 4),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task wrong_order_in_original_stream_b() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", eventNumber: 0),
					Rec.Write(t++, "ab-1", eventNumber: 0),
					Rec.Write(t++, "ab-1", eventNumber: 0),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					// they all have the same eventnumber so they will all be kept
					x.Recs[0].KeepIndexes(0, 1, 2, 3),
					x.Recs[1],
				},
				x => new[] {
					// however, only 0 and 3 can be found in the index when looking up by eventNumber
					// since 0, 1 and 2 all have event number 0.
					x.Recs[0].KeepIndexes(0, 3),
					x.Recs[1],
				});
	}
}
