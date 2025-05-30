// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.RequestManager.Managers;

public class DeleteStream : RequestManagerBase {
	private readonly bool _hardDelete;
	private readonly CancellationToken _cancellationToken;
	private readonly string _streamId;
	private readonly long _expectedVersion;

	public DeleteStream(
				IPublisher publisher,
				TimeSpan timeout,
				IEnvelope clientResponseEnvelope,
				Guid internalCorrId,
				Guid clientCorrId,
				string streamId,
				long expectedVersion,
				bool hardDelete,
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
		_hardDelete = hardDelete;
		_cancellationToken = cancellationToken;
		_streamId = streamId;
		_expectedVersion = expectedVersion;
	}

	protected override Message WriteRequestMsg =>
		new StorageMessage.WriteDelete(
				InternalCorrId,
				WriteReplyEnvelope,
				_streamId,
				_expectedVersion,
				_hardDelete,
				_cancellationToken);

	protected override Message ClientSuccessMsg =>
		 new ClientMessage.DeleteStreamCompleted(
			 ClientCorrId,
			 OperationResult.Success,
			 null,
			 LastEventNumbers.Single,
			 CommitPosition,  //not technically correct, but matches current behavior correctly
			 CommitPosition);

	protected override Message ClientFailMsg =>
		new ClientMessage.DeleteStreamCompleted(ClientCorrId, Result, FailureMessage,
			FailureCurrentVersions.Length is 1
				? FailureCurrentVersions.Single
				: -1 /* for backwards compatibility */);
}
