// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Core.Tests.Services.Replication;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using OperationResult = KurrentDB.Core.Messages.OperationResult;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_the_checkpoint_write_times_out<TLogFormat, TStreamId> :
	TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string CheckpointStream = "$projections-projection-checkpoint";
	private CoreProjectionCheckpointWriter _checkpointWriter;
	private FakeEnvelope _envelope;

	protected override void Given() {
		AllWritesQueueUp();
		NoStream(CheckpointStream);
	}

	[SetUp]
	public void setup() {
		_envelope = new FakeEnvelope();
		_checkpointWriter = new CoreProjectionCheckpointWriter(
			CheckpointStream, _ioDispatcher, new ProjectionVersion(1, 0, 0), "projection");
		// the metadata stream has already been written, so the checkpoint event is written straight away
		_checkpointWriter.StartFrom(CheckpointTag.FromStreamPosition(0, "stream", 10), checkpointEventNumber: 0);
		_checkpointWriter.BeginWriteCheckpoint(
			_envelope, CheckpointTag.FromStreamPosition(0, "stream", 11), "{}");

		CompleteWriteWithResult(OperationResult.CommitTimeout);
	}

	[Test]
	public void should_retry_the_write_until_the_retry_limit_is_reached() {
		var current = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Last();
		while (_consumer.HandledMessages.Last() is TimerMessage.Schedule schedule) {
			schedule.Envelope.ReplyWith(schedule.ReplyMessage);

			CompleteWriteWithResult(OperationResult.CommitTimeout);

			var last = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Last();
			Assert.AreEqual(current.EventStreamId, last.EventStreamId);
			Assert.AreEqual(current.Events, last.Events);
			current = last;
		}

		Assert.AreEqual(1, _envelope.Replies.OfType<CoreProjectionProcessingMessage.Failed>().Count(),
			"Should fail the projection after exhausting all the checkpoint write retries");
	}
}
