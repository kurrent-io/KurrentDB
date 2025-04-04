// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

// these systemtically exercise the cases in the IndexExecutor
// we still do so by testing high level scavenge cases because we are well geared up
// for that and testing the IndexExeecutor directly would involve more mocks than it is worth.
public class IndexExecutorTests : SqliteDbPerTest<IndexExecutorTests> {
	[Fact]
	public async Task nothing_to_scavenge() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 1, 2),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task simple_scavenge() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(1, 2, 3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task with_collision() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "cb-2"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(1, 2, 3, 4),
				x.Recs[1],
			});
	}
}
