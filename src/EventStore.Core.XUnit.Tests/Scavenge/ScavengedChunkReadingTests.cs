// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ScavengedChunkReadingTests : SqliteDbPerTest<ScavengedChunkReadingTests> {
	// This test produces a scavenged chunk that used to have a missing midpoint
	// (and, like in all the scavenge tests, verifies all the records can be read from it)
	// It turns out that the missing midpoint only made the midpoints less effective and did not cause
	// incorrect reads. The midpoints are accessed twice in LocatePosRange, once to get the lower bound
	// and once to get the upper bound.
	// If we hit the missing midpoint while finding the upper bound it will push the upper bound higher.
	// If we hit the missing midpoint while finding the lower bound it will set the lower bound to 0
	// because that was the value of the missing midpoint.
	// In both cases it makes the search wider.
	[Fact]
	public async Task missing_midpoint() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.SkipIndexCheck() // for speed
			.WithDb(x => x
				.Chunk([
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					..Enumerable.Range(0, 2738)
						.Select(_ => Rec.Write(t++, "cd-1")),
				])
				.Chunk(
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: SoftDelete),
					ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => [
				x.Recs[0][1..],
				x.Recs[1],
			]);
	}
}
