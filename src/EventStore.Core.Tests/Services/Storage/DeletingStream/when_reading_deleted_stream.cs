// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_deleted_stream<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private Guid _id1;
	private Guid _id2;
	private Guid _id3;
	private Guid _deleteId;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_id1 = Guid.NewGuid();
		_id2 = Guid.NewGuid();
		_id3 = Guid.NewGuid();
		_deleteId = Guid.NewGuid();
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var (streamId, pos0) = await GetOrReserve("ES", token);
		var (_, pos1) = await Writer.Write(LogRecord.SingleWrite(_recordFactory, pos0, _id1, _id1, streamId, ExpectedVersion.NoStream,
			eventTypeId, new byte[0], new byte[0], DateTime.UtcNow, PrepareFlags.IsCommitted), token);
		var (_, pos2) = await Writer.Write(LogRecord.SingleWrite(_recordFactory, pos1, _id2, _id2, streamId, 0,
			eventTypeId, new byte[0], new byte[0], DateTime.UtcNow, PrepareFlags.IsCommitted), token);
		var (_, pos3) = await Writer.Write(LogRecord.SingleWrite(_recordFactory, pos2, _id3, _id3, streamId, 1,
			eventTypeId, new byte[0], new byte[0], DateTime.UtcNow, PrepareFlags.IsCommitted), token);
		await Writer.Write(LogRecord.DeleteTombstone(_recordFactory, pos3, _deleteId, _deleteId, streamId,
			eventTypeId, EventNumber.DeletedStream - 1, PrepareFlags.IsCommitted), token);
	}

	[Test]
	public async Task the_stream_is_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES", CancellationToken.None));
	}

	[Test]
	public async Task the_last_event_number_is_deleted_stream() {
		Assert.AreEqual(EventNumber.DeletedStream, await ReadIndex.GetStreamLastEventNumber("ES", CancellationToken.None));
	}
}
