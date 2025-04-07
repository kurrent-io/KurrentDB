// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.RequestManager.Managers;

public class TransactionStart : RequestManagerBase {
	private readonly string _streamId;

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
				 expectedVersion,
				 commitSource,
				 prepareCount: 1) {
		_streamId = streamId;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WriteTransactionStart(
				InternalCorrId,
				WriteReplyEnvelope,
				_streamId,
				ExpectedVersion,
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
