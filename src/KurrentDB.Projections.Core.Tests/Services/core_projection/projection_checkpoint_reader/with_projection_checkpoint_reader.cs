// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests.Helpers.IODispatcherTests;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.projection_checkpoint_reader;

public abstract class with_projection_checkpoint_reader<TLogFormat, TStreamId> : IHandle<ClientMessage.ReadStreamEventsBackward> {
	protected readonly string _projectionCheckpointStreamId = "projection-checkpoint-stream";
	protected readonly Guid _projectionId = Guid.NewGuid();

	protected SynchronousScheduler _bus = new();
	protected IODispatcher _ioDispatcher;
	protected ProjectionVersion _projectionVersion;
	protected CoreProjectionCheckpointReader _reader;

	[OneTimeSetUp]
	public void TestFixtureSetUp() {
		_ioDispatcher = new IODispatcher(_bus, _bus, true);
		IODispatcherTestHelpers.SubscribeIODispatcher(_ioDispatcher, _bus);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);
		_projectionVersion = new ProjectionVersion(1, 2, 3);
		_reader = new(_bus, _projectionId, _ioDispatcher, _projectionCheckpointStreamId, _projectionVersion, true);
		When();
	}

	protected abstract void When();

	public virtual void Handle(ClientMessage.ReadStreamEventsBackward message) {
		var evnts = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId,
			ProjectionEventTypes.ProjectionCheckpoint, "[]",
			"""
			{
			  "$v": "1:-1:3:3",
			  "$c": 269728,
			  "$p": 269728
			}
			""");
		var reply = new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId,
			message.EventStreamId, message.FromEventNumber, message.MaxCount, ReadStreamResult.Success,
			evnts, null, true, "", 0, 0, true, 10000);
		message.Envelope.ReplyWith(reply);
	}
}
