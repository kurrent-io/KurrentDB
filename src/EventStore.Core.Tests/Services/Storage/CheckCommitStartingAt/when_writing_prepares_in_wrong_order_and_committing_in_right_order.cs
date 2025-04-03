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
public class when_writing_prepares_in_wrong_order_and_committing_in_right_order<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private IPrepareLogRecord _prepare0;
	private IPrepareLogRecord _prepare1;
	private IPrepareLogRecord _prepare2;
	private IPrepareLogRecord _prepare3;
	private IPrepareLogRecord _prepare4;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_prepare0 = await WritePrepare("ES", expectedVersion: -1, token: token);
		_prepare1 = await WritePrepare("ES", expectedVersion: 2, token: token);
		_prepare2 = await WritePrepare("ES", expectedVersion: 0, token: token);
		_prepare3 = await WritePrepare("ES", expectedVersion: 1, token: token);
		_prepare4 = await WritePrepare("ES", expectedVersion: 3, token: token);
		await WriteCommit(_prepare0.LogPosition, "ES", eventNumber: 0, token: token);
		await WriteCommit(_prepare2.LogPosition, "ES", eventNumber: 1, token: token);
		await WriteCommit(_prepare3.LogPosition, "ES", eventNumber: 2, token: token);
	}

	[Test]
	public async Task check_commmit_on_expected_prepare_should_return_ok_decision() {
		var res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare1.LogPosition,
			WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.Ok, res.Decision);
		Assert.AreEqual("ES", res.EventStreamId);
		Assert.AreEqual(2, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);
	}

	[Test]
	public async Task check_commmit_on_not_expected_prepare_should_return_wrong_expected_version() {
		var res = await ReadIndex.IndexWriter.CheckCommitStartingAt(_prepare4.LogPosition,
			WriterCheckpoint.ReadNonFlushed(), CancellationToken.None);

		Assert.AreEqual(CommitDecision.WrongExpectedVersion, res.Decision);
		Assert.AreEqual("ES", res.EventStreamId);
		Assert.AreEqual(2, res.CurrentVersion);
		Assert.AreEqual(-1, res.StartEventNumber);
		Assert.AreEqual(-1, res.EndEventNumber);
	}
}
