// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.RequestManager.Managers;

public class WriteEvents : RequestManagerBase {
	private readonly LowAllocReadOnlyMemory<string> _streamIds;
	private readonly LowAllocReadOnlyMemory<long> _expectedVersions;
	private readonly LowAllocReadOnlyMemory<Event> _events;
	private readonly LowAllocReadOnlyMemory<int> _eventStreamIndexes;
	private readonly CancellationToken _cancellationToken;

	public WriteEvents(IPublisher publisher,
		TimeSpan timeout,
		IEnvelope clientResponseEnvelope,
		Guid internalCorrId,
		Guid clientCorrId,
		LowAllocReadOnlyMemory<string> streamIds,
		LowAllocReadOnlyMemory<long> expectedVersions,
		LowAllocReadOnlyMemory<Event> events,
		LowAllocReadOnlyMemory<int> eventStreamIndexes,
		CommitSource commitSource,
		CancellationToken cancellationToken = default)
		: base(
				 publisher,
				 timeout,
				 clientResponseEnvelope,
				 internalCorrId,
				 clientCorrId,
				 commitSource,
				 prepareCount: 0,
				 waitForCommit: true) {
		_streamIds = streamIds;
		_expectedVersions = expectedVersions;
		_events = events;
		_eventStreamIndexes = eventStreamIndexes;
		_cancellationToken = cancellationToken;
	}

	// used in tests only
	public static WriteEvents ForSingleStream(
		IPublisher publisher,
		TimeSpan timeout,
		IEnvelope clientResponseEnvelope,
		Guid internalCorrId,
		Guid clientCorrId,
		string streamId,
		long expectedVersion,
		LowAllocReadOnlyMemory<Event> events,
		CommitSource commitSource,
		CancellationToken cancellationToken = default) {
		return new WriteEvents(
			publisher: publisher,
			timeout: timeout,
			clientResponseEnvelope: clientResponseEnvelope,
			internalCorrId: internalCorrId,
			clientCorrId: clientCorrId,
			streamIds: new(streamId),
			expectedVersions: new(expectedVersion),
			events: events,
			eventStreamIndexes: [],
			commitSource: commitSource,
			cancellationToken: cancellationToken);
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WritePrepares(
				InternalCorrId,
				WriteReplyEnvelope,
				_streamIds,
				_expectedVersions,
				_events,
				_eventStreamIndexes,
				_cancellationToken);


	protected override Message ClientSuccessMsg =>
		 new ClientMessage.WriteEventsCompleted(
			 ClientCorrId,
			 FirstEventNumbers,
			 LastEventNumbers,
			 CommitPosition,  //not technically correct, but matches current behavior correctly
			 CommitPosition);

	protected override Message ClientFailMsg =>
		 new ClientMessage.WriteEventsCompleted(
			 ClientCorrId,
			 Result,
			 FailureMessage,
			 FailureStreamIndexes,
			 FailureCurrentVersions);
}
