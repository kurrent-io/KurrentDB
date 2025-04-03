// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.IndexCommitter;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class  when_index_committer_service_receives_duplicate_commit_acks<TLogFormat, TStreamId> : with_index_committer_service<TLogFormat, TStreamId> {
	private readonly long _logPosition = 4000;
	private readonly Guid _correlationId = Guid.NewGuid();
	public override void Given() { }
	public override void When() {
		AddPendingPrepare(_logPosition);

		Service.Handle(new StorageMessage.CommitAck(_correlationId, _logPosition, _logPosition, 0, 0));
		Service.Handle(new StorageMessage.CommitAck(_correlationId, _logPosition, _logPosition, 0, 0));
		Service.Handle(new ReplicationTrackingMessage.ReplicatedTo( _logPosition));
	}

	[Test]
	public void commit_replicated_message_should_not_be_sent() {
		Assert.AreEqual(0, CommitReplicatedMgs.Count);
	}
	[Test]
	public void index_written_message_should_not_have_been_published() {
		Assert.AreEqual(0, IndexWrittenMgs.Count);
	}
	[Test]
	public void index_should_not_have_been_updated() {
		Assert.AreEqual(0, IndexCommitter.CommittedPrepares.Count);
	}
}
