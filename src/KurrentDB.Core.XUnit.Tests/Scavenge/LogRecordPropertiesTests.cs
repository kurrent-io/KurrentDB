// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Threading.Tasks;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TransactionLog.Scavenging.Helpers;
using KurrentDB.Core.XUnit.Tests.Scavenge.Infrastructure;
using KurrentDB.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static KurrentDB.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace KurrentDB.Core.XUnit.Tests.Scavenge;

public class LogRecordPropertiesTests : SqliteDbPerTest<LogRecordPropertiesTests> {
	[Fact]
	public async Task scavenge_works_when_properties_are_present() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1", properties: Encoding.UTF8.GetBytes("dummy-properties1")),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1", properties: Encoding.UTF8.GetBytes("dummy-properties2")),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "$$ab-1", "$metadata", metadata: MaxCount2))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			// the run ensures that surviving log records are equal to their originals
			.RunAsync(
				x => [
					x.Recs[0].KeepIndexes(2, 3, 4),
					x.Recs[1],
				]);
	}
}
