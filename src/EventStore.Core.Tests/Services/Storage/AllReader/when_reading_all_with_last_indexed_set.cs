// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.AllReader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_all<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {

	protected override void WriteTestScenario() {
		WritePrepare("ES1", 0, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
		WritePrepare("ES2", 0, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
		WritePrepare("ES2", 1, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
	}

	[Test]
	public void should_be_able_to_read_all_backwards() {
		var checkpoint = WriterCheckpoint.Read();
		var pos = new TFPos(checkpoint, checkpoint);
		var result = ReadIndex.ReadAllEventsBackward(pos, 10).EventRecords();
		Assert.AreEqual(3, result.Count);
	}

	[Test]
	public void should_be_able_to_read_all_forwards() {
		var result = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).EventRecords();
		Assert.AreEqual(3, result.Count);
	}
}
