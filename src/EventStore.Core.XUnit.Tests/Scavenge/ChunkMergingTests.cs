// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ChunkMergingTests : SqliteDbPerTest<ChunkMergingTests> {
	[Fact]
	public async Task can_merge() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithMergeChunks(true)
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-2")) // keep
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"), // keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1)) // keep
				.Chunk(ScavengePointRec(t++))) // keep
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new ILogRecord[][] {
					// chunk 0, 1 and 2 are the same chunk now
					new[] {
						x.Recs[0][1],
						x.Recs[1][1],
						x.Recs[1][2],
						x.Recs[2][0],
					},
					new[] {
						x.Recs[0][1],
						x.Recs[1][1],
						x.Recs[1][2],
						x.Recs[2][0],
					},
					new[] {
						x.Recs[0][1],
						x.Recs[1][1],
						x.Recs[1][2],
						x.Recs[2][0],
					}
				},
				x => null);
	}

	[Fact]
	public async Task can_not_merge() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cd-2"))
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount1))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				// chunks not merged
				x.Recs[0].KeepIndexes(1),
				x.Recs[1].KeepIndexes(1, 2),
				x.Recs[2].KeepIndexes(0),
			});
	}
}
