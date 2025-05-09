// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.RequestManager.Managers;

public class TransactionStart : RequestManagerBase {
	private readonly string _streamId;
	private readonly long _expectedVersion;

	public TransactionStart(
				IPublisher publisher,
				TimeSpan timeout,
				IEnvelope clientResponseEnvelope,
				Guid internalCorrId,
				Guid clientCorrId,
				string streamId,
				long expectedVersion,
				CommitSource commitSource)
		: base(
				 publisher,
				 timeout,
				 clientResponseEnvelope,
				 internalCorrId,
				 clientCorrId,
				 commitSource,
				 prepareCount: 1) {
		_streamId = streamId;
		_expectedVersion = expectedVersion;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WriteTransactionStart(
				InternalCorrId,
				WriteReplyEnvelope,
				_streamId,
				_expectedVersion,
				LiveUntil);

	protected override void AllEventsWritten() {
		if (CommitSource.ReplicationPosition >= LastEventPosition) {
			Committed();
		} else if (!Registered) {
			CommitSource.NotifyFor(LastEventPosition, Committed, CommitLevel.Replicated);
			Registered = true;
		}
	}

	protected override Message ClientSuccessMsg =>
		 new ClientMessage.TransactionStartCompleted(
					ClientCorrId,
					TransactionId,
					OperationResult.Success,
					null);

	protected override Message ClientFailMsg =>
		 new ClientMessage.TransactionStartCompleted(
					ClientCorrId,
					TransactionId,
					Result,
					FailureMessage);

}
