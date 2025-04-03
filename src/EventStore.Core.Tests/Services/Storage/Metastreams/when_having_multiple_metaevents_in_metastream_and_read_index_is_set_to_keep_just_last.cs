// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Metastreams;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_having_multiple_metaevents_in_metastream_and_read_index_is_set_to_keep_just_last<TLogFormat, TStreamId>
	: SimpleDbTestScenario<TLogFormat, TStreamId> {
	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator.Chunk(Rec.Prepare(0, "$$test", "0", metadata: new StreamMetadata(maxCount: 10)),
				Rec.Prepare(0, "$$test", "1", metadata: new StreamMetadata(maxCount: 9)),
				Rec.Prepare(0, "$$test", "2", metadata: new StreamMetadata(maxCount: 8)),
				Rec.Prepare(0, "$$test", "3", metadata: new StreamMetadata(maxCount: 7)),
				Rec.Prepare(0, "$$test", "4", metadata: new StreamMetadata(maxCount: 6)),
				Rec.Commit(0, "$$test"))
			.CreateDb(token: token);
	}

	[Test]
	public async Task last_event_read_returns_correct_event() {
		var res = await ReadIndex.ReadEvent("$$test", -1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual("4", res.Record.EventType);
	}

	[Test]
	public async Task last_event_stream_number_is_correct() {
		Assert.AreEqual(4, await ReadIndex.GetStreamLastEventNumber("$$test", CancellationToken.None));
	}

	[Test]
	public async Task single_event_read_returns_only_last_event() {
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent("$$test", 0, CancellationToken.None)).Result);
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent("$$test", 1, CancellationToken.None)).Result);
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent("$$test", 2, CancellationToken.None)).Result);
		Assert.AreEqual(ReadEventResult.NotFound, (await ReadIndex.ReadEvent("$$test", 3, CancellationToken.None)).Result);

		var res = await ReadIndex.ReadEvent("$$test", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual("4", res.Record.EventType);
	}

	[Test]
	public async Task stream_read_forward_returns_only_last_event() {
		var res = await ReadIndex.ReadStreamEventsForward("$$test", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(1, res.Records.Length);
		Assert.AreEqual("4", res.Records[0].EventType);
	}

	[Test]
	public async Task stream_read_backward_returns_only_last_event() {
		var res = await ReadIndex.ReadStreamEventsBackward("$$test", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(1, res.Records.Length);
		Assert.AreEqual("4", res.Records[0].EventType);
	}

	[Test]
	public async Task metastream_metadata_is_correct() {
		var metadata = await ReadIndex.GetStreamMetadata("$$test", CancellationToken.None);
		Assert.AreEqual(1, metadata.MaxCount);
		Assert.AreEqual(null, metadata.MaxAge);
	}

	[Test]
	public async Task original_stream_metadata_is_taken_from_last_metaevent() {
		var metadata = await ReadIndex.GetStreamMetadata("test", CancellationToken.None);
		Assert.AreEqual(6, metadata.MaxCount);
		Assert.AreEqual(null, metadata.MaxAge);
	}
}
