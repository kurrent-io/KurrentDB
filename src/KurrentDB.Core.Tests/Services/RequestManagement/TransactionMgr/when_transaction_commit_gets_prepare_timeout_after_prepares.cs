// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_commit_gets_prepare_timeout_after_prepares : RequestManagerSpecification<TransactionCommit> {

	private int transactionId = 2341;
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
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, transactionId, PrepareFlags.SingleWrite);
	}

	protected override Message When() {
		return new StorageMessage.RequestManagerTimerTick(
			DateTime.UtcNow + TimeSpan.FromTicks(CommitTimeout.Ticks / 2));
	}

	[Test]
	public void no_messages_are_published() {
		Assert.That(Produced.Count == 0);
	}

	[Test]
	public void the_envelope_is_not_replied_to() {
		Assert.AreEqual(0, Envelope.Replies.Count);
	}
}
