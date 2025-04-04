// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Chaser;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_chaser_reads_system_event<TLogFormat, TStreamId> : with_storage_chaser_service<TLogFormat, TStreamId> {
	private Guid _epochId;
	private int _epochNumber;

	public override async ValueTask When(CancellationToken token) {
		_epochId = Guid.NewGuid();
		_epochNumber = 7;
		var epoch = new EpochRecord(0, _epochNumber, _epochId, -1, DateTime.UtcNow, Guid.Empty);
		var rec = new SystemLogRecord(epoch.EpochPosition, epoch.TimeStamp, SystemRecordType.Epoch,
			SystemRecordSerialization.Json, epoch.AsSerialized());

		Assert.True(await Writer.Write(rec, token) is (true, _));
		await Writer.Flush(token);
	}
	[Test]
	public void epoch_should_be_updated() {
		AssertEx.IsOrBecomesTrue(() => EpochManager.GetLastEpoch() != null);
		Assert.AreEqual(_epochId, EpochManager.GetLastEpoch().EpochId);
		Assert.AreEqual(_epochNumber, EpochManager.GetLastEpoch().EpochNumber);
	}
}
