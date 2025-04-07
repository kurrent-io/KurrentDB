// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Metastreams;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_having_multiple_metaevents_in_metastream_and_read_index_is_set_to_keep_last_2<TLogFormat, TStreamId> : SimpleDbTestScenario<TLogFormat, TStreamId> {
	public when_having_multiple_metaevents_in_metastream_and_read_index_is_set_to_keep_last_2()
		: base(metastreamMaxCount: 2) {
	}

	protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
		return dbCreator.Chunk(
				Rec.Prepare(0, "$$test", "0", metadata: new StreamMetadata(10, null, null, null, null)),
				Rec.Prepare(0, "$$test", "1", metadata: new StreamMetadata(9, null, null, null, null)),
				Rec.Prepare(0, "$$test", "2", metadata: new StreamMetadata(8, null, null, null, null)),
				Rec.Prepare(0, "$$test", "3", metadata: new StreamMetadata(7, null, null, null, null)),
				Rec.Prepare(0, "$$test", "4", metadata: new StreamMetadata(6, null, null, null, null)),
				Rec.Commit(0, "$$test"))
			.CreateDb();
	}

	[Test]
	public void last_event_read_returns_correct_event() {
		var res = ReadIndex.ReadEvent("$$test", -1);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual("4", res.Record.EventType);
	}

	[Test]
	public void last_event_stream_number_is_correct() {
		Assert.AreEqual(4, ReadIndex.GetStreamLastEventNumber("$$test"));
	}

	[Test]
	public void single_event_read_returns_last_two_events() {
		Assert.AreEqual(ReadEventResult.NotFound, ReadIndex.ReadEvent("$$test", 0).Result);
		Assert.AreEqual(ReadEventResult.NotFound, ReadIndex.ReadEvent("$$test", 1).Result);
		Assert.AreEqual(ReadEventResult.NotFound, ReadIndex.ReadEvent("$$test", 2).Result);

		var res = ReadIndex.ReadEvent("$$test", 3);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual("3", res.Record.EventType);

		res = ReadIndex.ReadEvent("$$test", 4);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual("4", res.Record.EventType);
	}

	[Test]
	public void stream_read_forward_returns_last_two_events() {
		var res = ReadIndex.ReadStreamEventsForward("$$test", 0, 100);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(2, res.Records.Length);
		Assert.AreEqual("3", res.Records[0].EventType);
		Assert.AreEqual("4", res.Records[1].EventType);
	}

	[Test]
	public void stream_read_backward_returns_last_two_events() {
		var res = ReadIndex.ReadStreamEventsBackward("$$test", -1, 100);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(2, res.Records.Length);
		Assert.AreEqual("4", res.Records[0].EventType);
		Assert.AreEqual("3", res.Records[1].EventType);
	}

	[Test]
	public void metastream_metadata_is_correct() {
		var metadata = ReadIndex.GetStreamMetadata("$$test");
		Assert.AreEqual(2, metadata.MaxCount);
		Assert.AreEqual(null, metadata.MaxAge);
	}

	[Test]
	public void original_stream_metadata_is_taken_from_last_metaevent() {
		var metadata = ReadIndex.GetStreamMetadata("test");
		Assert.AreEqual(6, metadata.MaxCount);
		Assert.AreEqual(null, metadata.MaxAge);
	}
}
