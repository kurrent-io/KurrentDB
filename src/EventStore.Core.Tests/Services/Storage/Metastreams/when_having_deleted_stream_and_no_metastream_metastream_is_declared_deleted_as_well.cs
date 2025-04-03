// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Metastreams;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_having_deleted_stream_and_no_metastream_metastream_is_declared_deleted_as_well<TLogFormat, TStreamId>
	: SimpleDbTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator.Chunk(Rec.Prepare(0, "test"),
				Rec.Commit(0, "test"),
				Rec.Delete(2, "test"),
				Rec.Commit(2, "test"))
			.CreateDb(token: token);
	}

	[Test]
	public async Task the_stream_is_deleted() {
		Assert.IsTrue(await ReadIndex.IsStreamDeleted("test", CancellationToken.None));
	}

	[Test]
	public async Task the_metastream_is_deleted() {
		Assert.IsTrue(await ReadIndex.IsStreamDeleted("$$test", CancellationToken.None));
	}

	[Test]
	public async Task get_last_event_number_reports_deleted_metastream() {
		Assert.AreEqual(EventNumber.DeletedStream, await ReadIndex.GetStreamLastEventNumber("$$test", CancellationToken.None));
	}

	[Test]
	public async Task single_event_read_reports_deleted_metastream() {
		Assert.AreEqual(ReadEventResult.StreamDeleted, (await ReadIndex.ReadEvent("$$test", 0, CancellationToken.None)).Result);
	}

	[Test]
	public async Task last_event_read_reports_deleted_metastream() {
		Assert.AreEqual(ReadEventResult.StreamDeleted, (await ReadIndex.ReadEvent("$$test", -1, CancellationToken.None)).Result);
	}

	[Test]
	public async Task read_stream_events_forward_reports_deleted_metastream() {
		Assert.AreEqual(ReadStreamResult.StreamDeleted, (await ReadIndex.ReadStreamEventsForward("$$test", 0, 100, CancellationToken.None)).Result);
	}

	[Test]
	public async Task read_stream_events_backward_reports_deleted_metastream() {
		Assert.AreEqual(ReadStreamResult.StreamDeleted,
			(await ReadIndex.ReadStreamEventsBackward("$$test", 0, 100, CancellationToken.None)).Result);
	}
}
