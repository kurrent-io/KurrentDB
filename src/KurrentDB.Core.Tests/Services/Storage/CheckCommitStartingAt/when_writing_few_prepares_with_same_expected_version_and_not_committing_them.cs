// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.CheckCommitStartingAt;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_few_prepares_with_same_expected_version_and_not_committing_them<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private IPrepareLogRecord _prepare0;
	private IPrepareLogRecord _prepare1;
	private IPrepareLogRecord _prepare2;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare0 = await WritePrepare("ES", -1, token: token);
		_prepare1 = await WritePrepare("ES", -1, token: token);
		_prepare2 = await WritePrepare("ES", -1, token: token);
	}

	[Test]
	public async Task every_prepare_can_be_committed() {
		var res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare0.LogPosition,
			WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		var streamId = _logFormat.StreamIds.LookupValue("ES");

		Assert.AreEqual(CommitDecision.Ok, res.Decision);
		Assert.AreEqual(streamId, res.EventStreamId);
		Assert.AreEqual(-1, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);

		res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare1.LogPosition, WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.Ok, res.Decision);
		Assert.AreEqual(streamId, res.EventStreamId);
		Assert.AreEqual(-1, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);

		res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare2.LogPosition, WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.Ok, res.Decision);
		Assert.AreEqual(streamId, res.EventStreamId);
		Assert.AreEqual(-1, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);
	}
}
