// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "No such thing as a V0 prepare in LogV3")]
public class when_stream_is_softdeleted_with_mixed_log_record_version_0_and_version_1<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	private const string _deletedStream = "test";
	private const string _deletedMetaStream = "$$test";
	private const string _keptStream = "other";

	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator.Chunk(
				Rec.Prepare(0, _deletedMetaStream, metadata: new StreamMetadata(tempStream: true),
					version: LogRecordVersion.LogRecordV0),
				Rec.Commit(0, _deletedMetaStream, version: LogRecordVersion.LogRecordV0),
				Rec.Prepare(1, _keptStream, version: LogRecordVersion.LogRecordV1),
				Rec.Commit(1, _keptStream, version: LogRecordVersion.LogRecordV1),
				Rec.Prepare(2, _keptStream, version: LogRecordVersion.LogRecordV0),
				Rec.Commit(2, _keptStream, version: LogRecordVersion.LogRecordV0),
				Rec.Prepare(3, _deletedStream, version: LogRecordVersion.LogRecordV1),
				Rec.Commit(3, _deletedStream, version: LogRecordVersion.LogRecordV0),
				Rec.Prepare(4, _deletedStream, version: LogRecordVersion.LogRecordV0),
				Rec.Commit(4, _deletedStream, version: LogRecordVersion.LogRecordV1),
				Rec.Prepare(5, _keptStream, version: LogRecordVersion.LogRecordV1),
				Rec.Commit(5, _keptStream, version: LogRecordVersion.LogRecordV1),
				Rec.Prepare(6, _deletedMetaStream,
					metadata: new StreamMetadata(truncateBefore: EventNumber.DeletedStream, tempStream: true),
					version: LogRecordVersion.LogRecordV1),
				Rec.Commit(6, _deletedMetaStream, version: LogRecordVersion.LogRecordV0))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		return new[] {
			new[] {
				dbResult.Recs[0][2],
				dbResult.Recs[0][3],
				dbResult.Recs[0][4],
				dbResult.Recs[0][5],
				dbResult.Recs[0][10],
				dbResult.Recs[0][11]
			}
		};
	}

	[Test]
	public async Task scavenging_goes_as_expected() {
		await CheckRecords();
	}

	[Test]
	public async Task the_stream_is_absent_logically() {
		Assert.AreEqual(ReadEventResult.NoStream, (await ReadIndex.ReadEvent(_deletedStream, 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.NoStream,
			(await ReadIndex.ReadStreamEventsForward(_deletedStream, 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.NoStream,
			(await ReadIndex.ReadStreamEventsBackward(_deletedStream, -1, 100, CancellationToken.None)).Result);
	}

	[Test]
	public async Task the_metastream_is_absent_logically() {
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent(_deletedMetaStream, 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success,
			(await ReadIndex.ReadStreamEventsForward(_deletedMetaStream, 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success,
			(await ReadIndex.ReadStreamEventsBackward(_deletedMetaStream, -1, 100, CancellationToken.None)).Result);
	}

	[Test]
	public async Task the_stream_is_absent_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.IsEmpty((await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == _deletedStream));
		Assert.IsEmpty((await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == _deletedStream));
	}

	[Test]
	public async Task the_metastream_is_absent_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.IsEmpty((await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == _deletedMetaStream));
		Assert.IsEmpty((await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records
			.Where(x => x.Event.EventStreamId == _deletedMetaStream));
	}

	[Test]
	public async Task the_kept_stream_is_present() {
		Assert.AreEqual(ReadEventResult.Success, (await ReadIndex.ReadEvent(_keptStream, 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsForward(_keptStream, 0, 100, CancellationToken.None)).Result);
		Assert.AreEqual(ReadStreamResult.Success, (await ReadIndex.ReadStreamEventsBackward(_keptStream, -1, 100, CancellationToken.None)).Result);
	}
}
