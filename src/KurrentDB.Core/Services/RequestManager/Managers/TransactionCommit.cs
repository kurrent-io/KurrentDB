// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.RequestManager.Managers;

public class TransactionCommit : RequestManagerBase,
	IHandle<StorageMessage.CommitIndexed> {
	private readonly TimeSpan _commitTimeout;
	private bool _transactionWritten;
	public TransactionCommit(
				IPublisher publisher,
				TimeSpan prepareTimeout,
				TimeSpan commitTimeout,
				IEnvelope clientResponseEnvelope,
				Guid internalCorrId,
				Guid clientCorrId,
				long transactionId,
				CommitSource commitSource)
		: base(
				 publisher,
				 prepareTimeout,
				 clientResponseEnvelope,
				 internalCorrId,
				 clientCorrId,
				 commitSource,
				 transactionId: transactionId,
				 prepareCount: 1,
				 waitForCommit: true) {
		_commitTimeout = commitTimeout;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WriteTransactionEnd(
				InternalCorrId,
				WriteReplyEnvelope,
				TransactionId,
				LiveUntil);

	protected override void AllPreparesWritten() {
		base.AllPreparesWritten();
		NextTimeoutTime = DateTime.UtcNow + _commitTimeout;
		Publisher.Publish(
			new StorageMessage.WriteCommit(
					InternalCorrId,
					WriteReplyEnvelope,
					TransactionId));
	}

	protected override Message ClientSuccessMsg =>
		 new ClientMessage.TransactionCommitCompleted(
				ClientCorrId,
				TransactionId,
				FirstEventNumbers.Span[0],
				LastEventNumbers.Span[0],
				CommitPosition,   //not technically correct, but matches current behavior correctly
				CommitPosition);

	protected override Message ClientFailMsg =>
		 new ClientMessage.TransactionCommitCompleted(
				ClientCorrId,
				TransactionId,
				Result,
				FailureMessage);

	public override void Handle(StorageMessage.CommitIndexed message) {
		base.Handle(message);
		_transactionWritten = true;
		Committed();
	}
	protected override void Committed() {
		if (!_transactionWritten)
			return;
		base.Committed();
	}
	protected override void ReturnCommitAt(long logPosition, ReadOnlyMemory<long> firstEvents, ReadOnlyMemory<long> lastEvents) {
		_transactionWritten = true;
		base.ReturnCommitAt(logPosition, firstEvents, lastEvents);
	}


}
