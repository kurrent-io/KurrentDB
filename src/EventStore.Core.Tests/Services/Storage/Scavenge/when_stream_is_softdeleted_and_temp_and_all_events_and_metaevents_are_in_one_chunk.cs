// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_stream_is_softdeleted_and_temp_and_all_events_and_metaevents_are_in_one_chunk<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(
				Rec.Prepare(0, "$$test", metadata: new StreamMetadata(tempStream: true)),
				Rec.Commit(0, "$$test"),
				Rec.Prepare(1, "test"),
				Rec.Commit(1, "test"),
				Rec.Prepare(2, "test"),
				Rec.Commit(2, "test"),
				Rec.Prepare(3, "$$test", metadata: new StreamMetadata(truncateBefore: EventNumber.DeletedStream, tempStream: true)),
				Rec.Commit(3, "$$test"))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
			return new[] { new ILogRecord[0] };
		}

		return new[] {
			new[] {
				dbResult.Recs[0][0], // "test" created
			}
		};
	}

	[Test]
	public async Task scavenging_goes_as_expected() {
		await CheckRecords();
	}

	[Test]
	public async Task the_stream_is_absent_logically() {
		Assert.AreEqual(ReadEventResult.NoStream, (await ReadIndex.ReadEvent("test", 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.NoStream, (await ReadIndex.ReadStreamEventsForward("test", 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.NoStream, (await ReadIndex.ReadStreamEventsBackward("test", -1, 100, CancellationToken.None)).Result);
	}

	[Test]
	public async Task the_metastream_is_absent_logically() {
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent("$$test", 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsForward("$$test", 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsBackward("$$test", -1, 100, CancellationToken.None)).Result);
	}

	[Test]
	public async Task the_stream_is_absent_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.IsEmpty((await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == "test"));
		Assert.IsEmpty((await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == "test"));
	}

	[Test]
	public async Task the_metastream_is_absent_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.IsEmpty((await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == "$$test"));
		Assert.IsEmpty((await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == "$$test"));
	}
}
