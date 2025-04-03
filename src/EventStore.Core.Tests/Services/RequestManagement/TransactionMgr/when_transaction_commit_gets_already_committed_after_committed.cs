// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using EventStore.Core.Services.RequestManager.Managers;

namespace EventStore.Core.Tests.Services.RequestManagement.TransactionMgr;

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
		yield return new StorageMessage.PrepareAck(InternalCorrId, transactionId, PrepareFlags.TransactionEnd);
		yield return new StorageMessage.CommitIndexed(Guid.NewGuid(), commitPosition, transactionId, 0, 10);
		yield return new ReplicationTrackingMessage.ReplicatedTo(commitPosition);
		yield return new StorageMessage.CommitIndexed(InternalCorrId,commitPosition, transactionId,0,0);
	}

	protected override Message When() {
		return new StorageMessage.AlreadyCommitted(InternalCorrId, "test123", 0, 1, commitPosition);
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
