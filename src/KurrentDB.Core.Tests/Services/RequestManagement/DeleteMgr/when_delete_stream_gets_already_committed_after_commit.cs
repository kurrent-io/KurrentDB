// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.DeleteMgr;

[TestFixture]
public class when_delete_stream_gets_already_committed_after_commit : RequestManagerSpecification<DeleteStream> {
	private long commitPosition = 1000;
	protected override DeleteStream OnManager(FakePublisher publisher) {
		return new DeleteStream(
			publisher,
			CommitTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			"test123",
			ExpectedVersion.Any,
			false,
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return StorageMessage.CommitIndexed.ForSingleStream(InternalCorrId, commitPosition, 2, 3, 3);
		yield return new ReplicationTrackingMessage.ReplicatedTo(commitPosition);
	}

	protected override Message When() {
		return StorageMessage.AlreadyCommitted.ForSingleStream(InternalCorrId, "test123", 0, 1, commitPosition);
	}

	[Test]
	public void successful_request_message_is_not_published() {
		Assert.That(!Produced.Any());
	}

	[Test]
	public void the_envelope_is_not_replied_to_with_success() {
		Assert.That(!Envelope.Replies.Any());
	}
}
