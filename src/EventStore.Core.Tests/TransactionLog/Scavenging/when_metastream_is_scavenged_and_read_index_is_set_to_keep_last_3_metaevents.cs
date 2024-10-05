// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_metastream_is_scavenged_and_read_index_is_set_to_keep_last_3_metaevents<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	public when_metastream_is_scavenged_and_read_index_is_set_to_keep_last_3_metaevents() :
		base(metastreamMaxCount: 3) {
	}

	protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
		return dbCreator
			.Chunk(Rec.Prepare(0, "$$bla"),
				Rec.Prepare(0, "$$bla"),
				Rec.Prepare(0, "$$bla"),
				Rec.Prepare(0, "$$bla"),
				Rec.Prepare(0, "$$bla"),
				Rec.Commit(0, "$$bla"))
			.CompleteLastChunk()
			.CreateDb();
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		return LogFormatHelper<TLogFormat, TStreamId>.IsV2
			? new[] { dbResult.Recs[0].Where((x, i) => i >= 2).ToArray() }
			: new[] { dbResult.Recs[0].Where((x, i) => i == 0 || i >= 3).ToArray() };
	}

	[Test]
	public async Task metastream_is_scavenged_correctly() {
		await CheckRecords();
	}
}
