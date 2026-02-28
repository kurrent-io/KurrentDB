// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messages;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.IndexCommitter;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_index_committer_service_commits_empty_transaction_at_end_of_log<TLogFormat, TStreamId> : with_index_committer_service<TLogFormat, TStreamId> {
	private readonly long _logPrePosition = 4000;
	private readonly long _logPostPosition = 4001;

	public override void Given() { }

	public override void When() {
		WriterCheckpoint.Write(_logPostPosition);
		WriterCheckpoint.Flush();

		// chase empty transaction into index committer service
		Service.AddPendingPrepare(
			transactionPosition: _logPrePosition,
			prepares: [],
			postPosition: _logPostPosition);

		Service.Handle(new StorageMessage.CommitChased(
			correlationId: Guid.NewGuid(),
			logPosition: _logPrePosition,
			transactionPosition: _logPrePosition));

		// replicate it
		ReplicationCheckpoint.Write(_logPostPosition);
		ReplicationCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicatedTo(_logPostPosition));
	}

	[Test]
	public void indexed_to_end_of_transaction_file_message_should_have_been_published() {
		AssertEx.IsOrBecomesTrue(() => 1 == CommitReplicatedMgs.Count);
		Assert.AreEqual(1, IndexedToEndOfTransactionFileMgs.Count);
	}
}
