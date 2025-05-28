// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_commit_gets_already_committed_after_committed : RequestManagerSpecification<TransactionCommit> {

	private int transactionId = 2341;
	private long commitPosition = 3000;
	protected override TransactionCommit OnManager(FakePublisher publisher) {
		return new TransactionCommit(
			publisher,
			PrepareTimeout,
			CommitTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			transactionId,
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, transactionId, PrepareFlags.TransactionEnd);
		yield return StorageMessage.CommitIndexed.ForSingleStream(Guid.NewGuid(), commitPosition, transactionId, 0, 10);
		yield return new ReplicationTrackingMessage.ReplicatedTo(commitPosition);
		yield return StorageMessage.CommitIndexed.ForSingleStream(InternalCorrId, commitPosition, transactionId, 0, 0);
	}

	protected override Message When() {
		return StorageMessage.AlreadyCommitted.ForSingleStream(InternalCorrId, "test123", 0, 1, commitPosition);
	}

	[Test]
	public void successful_request_message_is_not_republished() {
		Assert.That(!Produced.Any());
	}

	[Test]
	public void the_envelope_is_not_replied_to_again() {
		Assert.That(!Envelope.Replies.Any());
	}
}
