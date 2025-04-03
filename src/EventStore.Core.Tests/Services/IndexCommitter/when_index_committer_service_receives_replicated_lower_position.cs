// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.Checkpoint;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.IndexCommitter;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_index_committer_service_receives_replicated_lower_position<TLogFormat, TStreamId>
	: with_index_committer_service<TLogFormat, TStreamId> {
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly long _logPosition = 2000;

	[OneTimeSetUp]
	public override Task TestFixtureSetUp() {
		ReplicationCheckpoint = new InMemoryCheckpoint(100);
		return base.TestFixtureSetUp();
	}
	public override void Given() { }
	public override void When() {
		Service.Handle(new StorageMessage.CommitAck(_correlationId, _logPosition, _logPosition, 0, 0));
		ReplicationCheckpoint.Write(_logPosition - 1);
		ReplicationCheckpoint.Flush();
		Service.Handle(new ReplicationTrackingMessage.ReplicatedTo(_logPosition - 1));
	}

	[Test]
	public void commit_replicated_message_should_not_have_been_published() {
		Assert.AreEqual(0, CommitReplicatedMgs.Count);
	}
	[Test]
	public void index_written_message_should_have_not_been_published() {
		Assert.AreEqual(0, IndexWrittenMgs.Count);
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
