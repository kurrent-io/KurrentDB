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
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "No such thing as a V0 prepare in LogV3")]
public class when_reading_deleted_stream_written_with_old_log_record_version<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private Guid _id1;
	private Guid _id2;
	private Guid _id3;
	private Guid _deleteId;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_id1 = Guid.NewGuid();
		_id2 = Guid.NewGuid();
		_id3 = Guid.NewGuid();
		_deleteId = Guid.NewGuid();

		var (_, pos1) = await Writer.Write(new PrepareLogRecord(0, _id1, _id1, 0, 0, "ES", null, 0, DateTime.UtcNow,
				PrepareFlags.SingleWrite, "type", null, new byte[0], new byte[0], LogRecordVersion.LogRecordV0),
			token);
		var (_, pos2) = await Writer.Write(new PrepareLogRecord(pos1, _id2, _id2, pos1, 0, "ES", null, 1, DateTime.UtcNow,
				PrepareFlags.SingleWrite, "type", null, new byte[0], new byte[0], LogRecordVersion.LogRecordV0),
			token);
		var (_, pos3) = await Writer.Write(new PrepareLogRecord(pos2, _id3, _id3, pos2, 0, "ES", null, 2, DateTime.UtcNow,
				PrepareFlags.SingleWrite, "type", null, new byte[0], new byte[0]),
			token);
		var (_, pos4) = await Writer.Write(new CommitLogRecord(pos3, _id1, 0, DateTime.UtcNow, 0, LogRecordVersion.LogRecordV0),
			token);
		var (_, pos5) = await Writer.Write(new CommitLogRecord(pos4, _id2, pos1, DateTime.UtcNow, 1, LogRecordVersion.LogRecordV0),
			token);
		var (_, pos6) = await Writer.Write(new CommitLogRecord(pos5, _id3, pos2, DateTime.UtcNow, 2, LogRecordVersion.LogRecordV0),
			token);


		var (_, pos7) = await Writer.Write(new PrepareLogRecord(pos6, _deleteId, _deleteId, pos6, 0, "ES", null, int.MaxValue - 1,
				DateTime.UtcNow,
				PrepareFlags.StreamDelete | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
				PrepareFlags.None,
				SystemEventTypes.StreamDeleted, null, Empty.ByteArray, Empty.ByteArray, LogRecordVersion.LogRecordV0),
			token);
		await Writer.Write(
			new CommitLogRecord(pos7, _deleteId, pos6, DateTime.UtcNow, int.MaxValue - 1,
				LogRecordVersion.LogRecordV0), token);
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
