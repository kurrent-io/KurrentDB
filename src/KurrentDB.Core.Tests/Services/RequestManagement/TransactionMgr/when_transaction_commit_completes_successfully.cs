// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_commit_completes_successfully : RequestManagerSpecification<TransactionCommit> {
	private long _commitPosition = 3000;
	private long _transactionPosition = 1000;
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
		yield return StorageMessage.ConsistencyChecksSucceeded.ForSingleStream(InternalCorrId, 0, 0);
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, _transactionPosition, PrepareFlags.TransactionEnd);
		yield return new StorageMessage.CommitChased(InternalCorrId, _commitPosition, _transactionPosition);
		yield return new ReplicationTrackingMessage.ReplicatedTo(_commitPosition);
	}

	protected override Message When() {
		return StorageMessage.CommitIndexed.ForSingleStream(InternalCorrId, _commitPosition, _transactionPosition);
	}

	[Test]
	public void successful_request_message_is_published() {
		Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
			x => x.CorrelationId == InternalCorrId && x.Success));
	}

	[Test]
	public void the_envelope_is_replied_to_with_success() {
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.TransactionCommitCompleted>(
			x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.Success));
	}
}
