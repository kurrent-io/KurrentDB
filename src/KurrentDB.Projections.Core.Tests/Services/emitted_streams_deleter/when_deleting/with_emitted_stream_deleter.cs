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
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

public abstract class with_emitted_stream_deleter<TLogFormat, TStreamId> : IHandle<ClientMessage.ReadStreamEventsForward>,
	IHandle<ClientMessage.ReadStreamEventsBackward>,
	IHandle<ClientMessage.DeleteStream> {
	protected readonly SynchronousScheduler _bus = new();
	private IODispatcher _ioDispatcher;
	protected EmittedStreamsDeleter _deleter;
	private ProjectionNamesBuilder _projectionNamesBuilder;
	private const string ProjectionName = "test_projection";
	protected string _checkpointName;
	protected string _testStreamName = "test_stream";
	private bool _hasReadForward;

	[OneTimeSetUp]
	protected virtual void SetUp() {
		_ioDispatcher = new IODispatcher(_bus, _bus, true);
		_projectionNamesBuilder = ProjectionNamesBuilder.CreateForTest(ProjectionName);
		_checkpointName = _projectionNamesBuilder.GetEmittedStreamsCheckpointName();

		_deleter = new(_ioDispatcher, _projectionNamesBuilder.GetEmittedStreamsName(), _checkpointName);

		IODispatcherTestHelpers.SubscribeIODispatcher(_ioDispatcher, _bus);

		_bus.Subscribe<ClientMessage.ReadStreamEventsForward>(this);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);
		_bus.Subscribe<ClientMessage.DeleteStream>(this);

		When();
	}

	public abstract void When();

	public virtual void Handle(ClientMessage.ReadStreamEventsBackward message) {
		var events = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId,
			ProjectionEventTypes.ProjectionCheckpoint, "0");
		var reply = new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId,
			message.EventStreamId, message.FromEventNumber, message.MaxCount,
			ReadStreamResult.Success, events, null, false, string.Empty, 0, message.FromEventNumber, true, 1000);

		message.Envelope.ReplyWith(reply);
	}

	public virtual void Handle(ClientMessage.ReadStreamEventsForward message) {
		ClientMessage.ReadStreamEventsForwardCompleted reply;

		if (!_hasReadForward) {
			_hasReadForward = true;
			var events = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId,
				ProjectionEventTypes.ProjectionCheckpoint, _testStreamName);
			reply = new(message.CorrelationId, message.EventStreamId,
				message.FromEventNumber, message.MaxCount,
				ReadStreamResult.Success, events, null, false, string.Empty, message.FromEventNumber + 1,
				message.FromEventNumber, true, 1000);
		} else {
			reply = new(message.CorrelationId, message.EventStreamId,
				message.FromEventNumber, message.MaxCount,
				ReadStreamResult.Success, [], null, false, string.Empty,
				message.FromEventNumber, message.FromEventNumber, true, 1000);
		}

		message.Envelope.ReplyWith(reply);
	}

	public abstract void Handle(ClientMessage.DeleteStream message);
}
