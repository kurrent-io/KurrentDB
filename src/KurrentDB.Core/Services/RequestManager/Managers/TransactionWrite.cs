// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.RequestManager.Managers;

public class TransactionWrite : RequestManagerBase {
	private static readonly Operation Operation = new Operation(Operations.Streams.Write);
	private readonly Event[] _events;
	private long _transactionId;

	public TransactionWrite(
				IPublisher publisher,
				TimeSpan timeout,
				IEnvelope clientResponseEnvelope,
				Guid internalCorrId,
				Guid clientCorrId,
				Event[] events,
				long transactionId,
				CommitSource commitSource)
		: base(
				 publisher,
				 timeout,
				 clientResponseEnvelope,
				 internalCorrId,
				 clientCorrId,
				 commitSource,
				 prepareCount: events.Length,
				 transactionId) {
		_events = events;
		_transactionId = transactionId;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WriteTransactionData(
				InternalCorrId,
				WriteReplyEnvelope,
				TransactionId,
				_events);

	protected override void AllEventsWritten() {
		if (CommitSource.ReplicationPosition >= LastEventPosition) {
			Committed();
		} else if (!Registered) {
			CommitSource.NotifyFor(LastEventPosition, Committed, CommitLevel.Replicated);
			Registered = true;
		}
	}

	protected override Message ClientSuccessMsg =>
		 new ClientMessage.TransactionWriteCompleted(
					ClientCorrId,
					TransactionId,
					OperationResult.Success,
					null);
	protected override Message ClientFailMsg =>
		 new ClientMessage.TransactionWriteCompleted(
					ClientCorrId,
					TransactionId,
					Result,
					FailureMessage);
}
