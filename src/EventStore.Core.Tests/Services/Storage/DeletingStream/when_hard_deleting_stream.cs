// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Services;
using System;
using EventStore.Core.TransactionLog;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.LogCommon;

namespace EventStore.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_hard_deleting_stream<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteSingleEvent("ES1", 0, new string('.', 3000), token: token);
		await WriteSingleEvent("ES1", 1, new string('.', 3000), token: token);
		await WriteDelete("ES1", token);
	}

	[Test]
	public async Task should_change_expected_version_to_deleted_event_number_when_reading() {
		var chunk = await Db.Manager.GetInitializedChunk(0, CancellationToken.None);
		var chunkRecords = new List<ILogRecord>();
		RecordReadResult result = await chunk.TryReadFirst(CancellationToken.None);
		while (result.Success) {
			chunkRecords.Add(result.LogRecord);
			result = await chunk.TryReadClosestForward(result.NextPosition, CancellationToken.None);
		}

		Assert.That(chunkRecords.Any(x =>
			x.RecordType == LogRecordType.Commit && ((CommitLogRecord)x).FirstEventNumber == long.MaxValue));
		Assert.That(chunkRecords.Any(x =>
			x.RecordType == LogRecordType.Prepare && ((IPrepareLogRecord<TStreamId>)x).ExpectedVersion == long.MaxValue - 1));
	}
}
