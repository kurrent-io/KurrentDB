// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.RequestManager.Managers;

public class WriteEvents : RequestManagerBase {
	private readonly string _streamId;
	private readonly Event[] _events;
	private readonly CancellationToken _cancellationToken;
	public WriteEvents(IPublisher publisher,
		TimeSpan timeout,
		IEnvelope clientResponseEnvelope,
		Guid internalCorrId,
		Guid clientCorrId,
		string streamId,
		long expectedVersion,
		Event[] events,
		CommitSource commitSource,
		CancellationToken cancellationToken = default)
		: base(
				 publisher,
				 timeout,
				 clientResponseEnvelope,
				 internalCorrId,
				 clientCorrId,
				 expectedVersion,
				 commitSource,
				 prepareCount: 0,
				 waitForCommit: true) {
		_streamId = streamId;
		_events = events;
		_cancellationToken = cancellationToken;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WritePrepares(
				InternalCorrId,
				WriteReplyEnvelope,
				_streamId,
				ExpectedVersion,
				_events,
				_cancellationToken);


	protected override Message ClientSuccessMsg =>
		 new ClientMessage.WriteEventsCompleted(
			 ClientCorrId,
			 FirstEventNumber,
			 LastEventNumber,
			 CommitPosition,  //not technically correct, but matches current behavior correctly
			 CommitPosition);

	protected override Message ClientFailMsg =>
		 new ClientMessage.WriteEventsCompleted(
			 ClientCorrId,
			 Result,
			 FailureMessage,
			 FailureCurrentVersion);
}
