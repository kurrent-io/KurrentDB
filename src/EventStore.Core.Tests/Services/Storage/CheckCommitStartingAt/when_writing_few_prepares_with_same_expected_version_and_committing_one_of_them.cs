// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.CheckCommitStartingAt;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class
	when_writing_few_prepares_with_same_expected_version_and_committing_one_of_them<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private IPrepareLogRecord _prepare0;
	private IPrepareLogRecord _prepare1;
	private IPrepareLogRecord _prepare2;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare0 = await WritePrepare("ES", expectedVersion: -1, token: token);
		_prepare1 = await WritePrepare("ES", expectedVersion: -1, token: token);
		_prepare2 = await WritePrepare("ES", expectedVersion: -1, token: token);
		await WriteCommit(_prepare1.LogPosition, "ES", eventNumber: 0, token: token);
	}

	[Test]
	public async Task other_prepares_cannot_be_committed() {
		var res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare0.LogPosition,
			WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.WrongExpectedVersion, res.Decision);
		Assert.AreEqual("ES", res.EventStreamId);
		Assert.AreEqual(0, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);

		res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare2.LogPosition, WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.WrongExpectedVersion, res.Decision);
		Assert.AreEqual("ES", res.EventStreamId);
		Assert.AreEqual(0, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);
	}
}
