// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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
public class
	when_stream_is_softdeleted_and_temp_with_log_version_0_but_some_events_are_in_multiple_chunks<TLogFormat, TStreamId> :
		ScavengeTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		var version = LogRecordVersion.LogRecordV0;
		return dbCreator.Chunk(Rec.Prepare(0, "test", version: version),
				Rec.Commit(0, "test", version: version))
			.Chunk(Rec.Prepare(1, "test", version: version),
				Rec.Commit(1, "test", version: version),
				Rec.Prepare(2, "$$test", metadata: new StreamMetadata(null, null, int.MaxValue, true, null, null),
					version: version),
				Rec.Commit(2, "$$test", version: version))
			.Chunk(
				Rec.Prepare(3, "random",
					version: version), // Need an incomplete chunk to ensure writer checkpoints are correct
				Rec.Commit(3, "random", version: version))
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		return new[] {
			new ILogRecord[0],
			new[] {
				dbResult.Recs[1][0],
				dbResult.Recs[1][1],
				dbResult.Recs[1][2],
				dbResult.Recs[1][3]
			},
			new[] {
				dbResult.Recs[2][0],
				dbResult.Recs[2][1]
			}
		};
	}

	[Test]
	public async Task scavenging_goes_as_expected() {
		await CheckRecords();
	}

	[Test]
	public async Task the_stream_is_absent_logically() {
		Assert.AreEqual(ReadStreamResult.NoStream, (await ReadIndex.ReadStreamEventsForward("test", 0, 100, CancellationToken.None)).Result,
			"Read test stream forward");
		Assert.AreEqual(ReadStreamResult.NoStream, (await ReadIndex.ReadStreamEventsBackward("test", -1, 100, CancellationToken.None)).Result,
			"Read test stream backward");
		Assert.AreEqual(ReadEventResult.NoStream, (await ReadIndex.ReadEvent("test", 0, CancellationToken.None)).Result,
			"Read single event from test stream");
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
			(await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
				.Count(x => x.Event.EventStreamId == "test"));
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsBackward(headOfTf, 1000, CancellationToken.None)).Records.Count(x =>
				x.Event.EventStreamId == "test"));
	}

	[Test]
	public async Task the_metastream_is_present_physically() {
		var headOfTf = new TFPos(Db.Config.WriterCheckpoint.Read(), Db.Config.WriterCheckpoint.Read());
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 1000, CancellationToken.None)).Records
				.Count(x => x.Event.EventStreamId == "$$test"), "Read $$test stream forward");
		Assert.AreEqual(1,
			(await ReadIndex.ReadAllEventsBackward(headOfTf, 10, CancellationToken.None)).Records.Count(x =>
				x.Event.EventStreamId == "$$test"),
			"Read $$test stream backward");
	}
}
