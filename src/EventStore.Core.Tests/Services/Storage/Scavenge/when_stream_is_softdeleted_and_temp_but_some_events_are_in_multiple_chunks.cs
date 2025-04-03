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
public class when_stream_is_softdeleted_and_temp_but_some_events_are_in_multiple_chunks_2<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(
				Rec.Prepare(0, "test"),
				Rec.Commit(0, "test"))
			.Chunk(
				Rec.Prepare(1, "test"),
				Rec.Commit(1, "test"),
				Rec.Prepare(2, "$$test", metadata: new StreamMetadata(null, null, EventNumber.DeletedStream, true, null, null)),
				Rec.Commit(2, "$$test"))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
			return new[] {
				new ILogRecord[0],
				new[] {
					dbResult.Recs[1][0],
					dbResult.Recs[1][1],
					dbResult.Recs[1][2],
					dbResult.Recs[1][3]
				}
			};
		}

		return new[] {
			new[] {
				dbResult.Recs[0][0], // "test" created
			},
			new[] {
				dbResult.Recs[1][0],
				dbResult.Recs[1][1],
				dbResult.Recs[1][2],
				dbResult.Recs[1][3]
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
	public async Task the_metastream_is_present_logically() {
		Assert.AreEqual(ReadEventResult.Success, (await ReadIndex.ReadEvent("$$test", -1, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsForward("$$test", 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(1, (await ReadIndex.ReadStreamEventsForward("$$test", 0, 100, CancellationToken.None)).Records.Length);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsBackward("$$test", -1, 100, CancellationToken.None)).Result);
		Assert.AreEqual(1, (await ReadIndex.ReadStreamEventsBackward("$$test", -1, 100, CancellationToken.None)).Records.Length);
	}

	[Test]
	public async Task the_stream_is_present_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None))
				.Records
				.Count(x => x.Event.EventStreamId == "test"));
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records.Count(x => x.Event.EventStreamId == "test"));
	}

	[Test]
	public async Task the_metastream_is_present_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None))
				.Records
				.Count(x => x.Event.EventStreamId == "$$test"));
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records.Count(x => x.Event.EventStreamId == "$$test"));
	}
}
