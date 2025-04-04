// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_stream_is_deleted_and_bulk_transaction_spans_chunks_boundary<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(Rec.Prepare(0, "bla"),
				Rec.Commit(0, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"))
			.Chunk(Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Commit(1, "bla"),
				Rec.Delete(2, "bla"),
				Rec.Commit(2, "bla"))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		return new[] {
			new[] {dbResult.Recs[0][2]}, // first prepare in commit that is in different chunk
			dbResult.Recs[1].Where((x, i) => i >= 3).ToArray(),
		};
	}

	[Test]
	public async Task first_prepare_of_transaction_is_preserved() {
		await CheckRecords();
	}
}
