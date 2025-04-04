// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.CheckCommitStartingAt;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_single_prepare<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private IPrepareLogRecord _prepare;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare = await WritePrepare("ES", -1, token: token);
	}

	[Test]
	public async Task check_commmit_should_return_ok_decision() {
		var res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare.LogPosition,
			WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		var streamId = _logFormat.StreamIds.LookupValue("ES");

		Assert.AreEqual(CommitDecision.Ok, res.Decision);
		Assert.AreEqual(streamId, res.EventStreamId);
		Assert.AreEqual(-1, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);
	}
}
