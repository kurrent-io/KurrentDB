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
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleted_stream_with_a_lot_of_data_is_scavenged<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(Rec.Prepare(0, "bla"),
				Rec.Prepare(0, "bla"),
				Rec.Commit(0, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Commit(1, "bla"),
				Rec.Delete(2, "bla"),
				Rec.Commit(2, "bla"))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		var keep = LogFormatHelper<TLogFormat, TStreamId>.IsV2
			? new int[] { 8, 9 }
			: new int[] { 0, 9, 10 }; // "bla" created

		return new[] {
			dbResult.Recs[0].Where((x, i) => keep.Contains(i)).ToArray(),
		};
	}

	[Test]
	public async Task only_delete_tombstone_records_with_their_commits_are_kept() {
		await CheckRecords();
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleted_stream_with_a_lot_of_data_is_scavenged_with_ingore_harddelete<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override bool UnsafeIgnoreHardDelete() {
		return true;
	}

	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(Rec.Prepare(0, "bla"),
				Rec.Prepare(0, "bla"),
				Rec.Commit(0, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Prepare(1, "bla"),
				Rec.Commit(1, "bla"),
				Rec.Delete(2, "bla"),
				Rec.Commit(2, "bla"))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		var keep = LogFormatHelper<TLogFormat, TStreamId>.IsV2
			? new int[] { }
			: new int[] { 0 }; // "bla" created

		return new[] {
			dbResult.Recs[0].Where((x, i) => keep.Contains(i)).ToArray(),
		};
	}

	[Test]
	public async Task only_delete_tombstone_records_with_their_commits_are_kept() {
		await CheckRecords();
	}
}
