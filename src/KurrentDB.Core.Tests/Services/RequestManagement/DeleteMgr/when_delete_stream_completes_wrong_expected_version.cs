// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.DeleteMgr;

[TestFixture]
public class when_delete_stream_completes_wrong_expected_version : RequestManagerSpecification<DeleteStream> {
	private readonly long _commitPosition = 1000;
	private readonly long _expectedVersion = 10;
	private readonly string _streamId = $"DeleteTest-{Guid.NewGuid()}";

	protected override DeleteStream OnManager(FakePublisher publisher) {
		return new DeleteStream(
			publisher,
			CommitTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			streamId: _streamId,
			expectedVersion: _expectedVersion,
			hardDelete: false,
			commitSource: CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return StorageMessage.CommitIndexed.ForSingleStream(InternalCorrId, _commitPosition, 2);
	}

	protected override Message When() {
		return StorageMessage.ConsistencyChecksFailed.ForSingleStream(InternalCorrId, _expectedVersion, 3, null);
	}

	[Test]
	public void failed_request_message_is_published() {
		Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
			x => x.CorrelationId == InternalCorrId && !x.Success));
	}

	[Test]
	public void the_envelope_is_replied_to_with_wrong_expected_version() {
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.DeleteStreamCompleted>(
			x => x.CorrelationId == ClientCorrId &&
				 x.Result == OperationResult.WrongExpectedVersion &&
				 x.CurrentVersion == 3));
	}
}
