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

#pragma warning disable CS0162 // Unreachable code detected

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ChunkWeightTests : SqliteDbPerTest<ChunkWeightTests> {
	[Fact]
	public async Task simple() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"), // weight: 2
					Rec.Write(t++, "ab-1"), // weight: 2
					Rec.Write(t++, "ab-1"))
				.Chunk(
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(4, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
			})
			.RunAsync();
	}

	[Fact]
	public async Task max_age_maybe_discard() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // weight: 1
					Rec.Write(t++, "ab-1", timestamp: Expired), // weight: 1
					Rec.Write(t++, "ab-1", timestamp: Active), // weight: 1
					Rec.Write(t++, "ab-1", timestamp: Active), // weight: 0 - last event
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxAgeMetadata))
				.Chunk(
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(3, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.True(state.TryGetOriginalStreamData("ab-1", out var data));
				Assert.Equal(DiscardPoint.DiscardBefore(0), data.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(3), data.MaybeDiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task non_contiguous_events() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", eventNumber: 0), // weight: 2
					Rec.Write(t++, "ab-1", eventNumber: 1), // weight: 2
					Rec.Write(t++, "ab-1", eventNumber: 5), // weight: 2
					Rec.Write(t++, "ab-1", eventNumber: 6), // weight: 2
					Rec.Write(t++, "ab-1", eventNumber: 7), // weight: 0 - last event
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(8, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.True(state.TryGetOriginalStreamData("ab-1", out var data));
				Assert.Equal(DiscardPoint.DiscardBefore(7), data.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(7), data.MaybeDiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task long_stream() {
		if (Scenario.CollideEverything) {
			// TODO: something weird happens here, possibly the system doesn't properly handle
			// $$$settings (or any metadatastream?) colliding with a long stream, which this scenario
			// causes.
			return;
		}

		var t = 0;

		var numRecords = 260;

		var records = new Rec[numRecords];
		for (var i = 0; i < numRecords; i++) {
			records[i] = Rec.Write(t++, "ab-1");
		}

		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					records)
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(numRecords * 2, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
				Assert.True(state.TryGetOriginalStreamData("ab-1", out var data));
				Assert.Equal(DiscardPoint.DiscardBefore(numRecords), data.DiscardPoint);
				Assert.Equal(DiscardPoint.DiscardBefore(numRecords), data.MaybeDiscardPoint);
			})
			.RunAsync();
	}

	[Fact]
	public async Task metadata_replaced_by_metadata() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1)) // weight: 2
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1),
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(2, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
			})
			.RunAsync();
	}

	[Fact]
	public async Task metadata_replaced_by_tombstone() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1)) // weight: 2
				.Chunk(
					Rec.CommittedDelete(t++, "ab-1"),
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(2, state.SumChunkWeights(0, 0));
				Assert.Equal(0, state.SumChunkWeights(1, 1));
			})
			.RunAsync();
	}

	[Fact]
	public async Task metadata_replaced_multiple_times() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1)) // weight: 2
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1), // weight: 2
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1)) // weight: 2
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1), // weight: 2
					Rec.CommittedDelete(t++, "ab-1"),
					ScavengePointRec(t++, threshold: 1000)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.Equal(2, state.SumChunkWeights(0, 0));
				Assert.Equal(4, state.SumChunkWeights(1, 1));
				Assert.Equal(2, state.SumChunkWeights(2, 2));
			})
			.RunAsync();
	}
}
