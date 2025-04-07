// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Metastreams;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	read_index_result_original_stream_exists_tests<TLogFormat, TStreamId>
	: SimpleDbTestScenario<TLogFormat, TStreamId> {

	protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
		return dbCreator.Chunk(
			Rec.Prepare(0, "existing_stream"),
			Rec.Commit(0, "existing_stream"),
			Rec.Prepare(1, "$existing_stream"),
			Rec.Commit(1, "$existing_stream")
		).CreateDb();
	}

	[Test]
	public void original_stream_exists_is_true_when_reading_metastream_for_existing_stream() {
		var metaStreamName = SystemStreams.MetastreamOf("existing_stream");
		var metaStreamId = _logFormat.StreamIds.LookupValue(metaStreamName);
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, metaStreamId, 0);
		Assert.True(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_false_when_reading_metastream_for_non_existent_stream() {
		var metaStreamName = SystemStreams.MetastreamOf("non_existent_stream");
		var metaStreamId = _logFormat.StreamIds.LookupValue(metaStreamName);
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, metaStreamId, 0);
		Assert.False(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_null_when_reading_existing_stream() {
		var streamId = _logFormat.StreamIds.LookupValue("existing_stream");
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, 0);
		Assert.IsNull(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_null_when_reading_non_existent_stream() {
		var streamId = _logFormat.StreamIds.LookupValue("non_existent_stream");
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, 0);
		Assert.IsNull(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_true_when_reading_metastream_for_existing_system_stream() {
		var metaStreamName = SystemStreams.MetastreamOf("$existing_stream");
		var metaStreamId = _logFormat.StreamIds.LookupValue(metaStreamName);
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, metaStreamId, 0);
		Assert.True(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_false_when_reading_metastream_for_non_existent_system_stream() {
		var metaStreamName = SystemStreams.MetastreamOf("$non_existent_stream");
		var metaStreamId = _logFormat.StreamIds.LookupValue(metaStreamName);
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, metaStreamId, 0);
		Assert.False(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_null_when_reading_existing_system_stream() {
		var streamId = _logFormat.StreamIds.LookupValue("$existing_stream");
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, 0);
		Assert.IsNull(read.OriginalStreamExists);
	}

	[Test]
	public void original_stream_exists_is_null_when_reading_non_existent_system_stream() {
		var streamId = _logFormat.StreamIds.LookupValue("$non_existent_stream");
		var read = ReadIndex.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, 0);
		Assert.IsNull(read.OriginalStreamExists);
	}
}
