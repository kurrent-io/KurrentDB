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
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;
#pragma warning disable CS0162 // Unreachable code detected

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class CombinationCriteriaTests : SqliteDbPerTest<CombinationCriteriaTests> {
	// in combination, we discard the event if any of the criteria says we can discard it.
	// 'then' means the latter taking priority
	[Fact]
	public async Task max_count_then_tombstone() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task max_count_discards_more_than_max_age() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 2
					Rec.Write(t++, "ab-1", timestamp: Active), // 3 <-- maxcount discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxAge: MaxAgeTimeSpan,
						maxCount: 1)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(4, 5),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task max_count_discards_more_than_truncate_before() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"), // 0
					Rec.Write(t++, "ab-1"), // 1 <-- tb discard
					Rec.Write(t++, "ab-1"), // 2
					Rec.Write(t++, "ab-1"), // 3 <-- maxcount discard
					Rec.Write(t++, "ab-1"), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						truncateBefore: 2,
						maxCount: 1)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(4, 5),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task max_count_discards_more_than_max_age_and_truncate_before() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 2
					Rec.Write(t++, "ab-1", timestamp: Active), // 3 <-- tb discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4
					Rec.Write(t++, "ab-1", timestamp: Active), // 5 <-- maxcount discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 6 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxAge: MaxAgeTimeSpan,
						truncateBefore: 4,
						maxCount: 1)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(6, 7),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task truncate_before_discards_more_than_max_count() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"), // 0
					Rec.Write(t++, "ab-1"), // 1 <-- maxcount discard
					Rec.Write(t++, "ab-1"), // 2
					Rec.Write(t++, "ab-1"), // 3 <-- tb discard
					Rec.Write(t++, "ab-1"), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxCount: 3,
						truncateBefore: 4)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(4, 5),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task truncate_before_discards_more_than_max_age() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 2
					Rec.Write(t++, "ab-1", timestamp: Active), // 3 <-- tb discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxAge: MaxAgeTimeSpan,
						truncateBefore: 4)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(4, 5),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task truncate_before_discards_more_than_max_age_and_max_count() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 2
					Rec.Write(t++, "ab-1", timestamp: Active), // 3 <-- maxcount discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4
					Rec.Write(t++, "ab-1", timestamp: Active), // 5 <-- tb discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 6 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxAge: MaxAgeTimeSpan,
						maxCount: 3,
						truncateBefore: 6)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(6, 7),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task max_age_discards_more_than_max_count() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxcount discard
					Rec.Write(t++, "ab-1", timestamp: Expired), // 2
					Rec.Write(t++, "ab-1", timestamp: Expired), // 3 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxCount: 3,
						maxAge: MaxAgeTimeSpan)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(4, 5),
					x.Recs[1],
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(2, 3, 4, 5),
						x.Recs[1],
					});
	}

	[Fact]
	public async Task max_age_discards_more_than_truncate_before() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- tb discard
					Rec.Write(t++, "ab-1", timestamp: Expired), // 2
					Rec.Write(t++, "ab-1", timestamp: Expired), // 3 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 4 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						truncateBefore: 2,
						maxAge: MaxAgeTimeSpan)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(4, 5),
					x.Recs[1],
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(2, 3, 4, 5),
						x.Recs[1],
					});
	}

	[Fact]
	public async Task max_age_discards_more_than_truncate_before_and_max_count() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", timestamp: Expired), // 0
					Rec.Write(t++, "ab-1", timestamp: Expired), // 1 <-- maxcount discard
					Rec.Write(t++, "ab-1", timestamp: Expired), // 2
					Rec.Write(t++, "ab-1", timestamp: Expired), // 3 <-- tb discard
					Rec.Write(t++, "ab-1", timestamp: Expired), // 4
					Rec.Write(t++, "ab-1", timestamp: Expired), // 5 <-- maxage discard
					Rec.Write(t++, "ab-1", timestamp: Active), // 6 <-- keep
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: new StreamMetadata(
						maxCount: 5,
						truncateBefore: 4,
						maxAge: MaxAgeTimeSpan)))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(6, 7),
					x.Recs[1],
				},
				Scenario.CollideEverything
					? default(Func<DbResult, LogRecord[][]>)
					: x => new[] {
						x.Recs[0].KeepIndexes(4, 5, 6, 7),
						x.Recs[1],
					});
	}
}
