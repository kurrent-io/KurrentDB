// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Helpers.IODispatcherTests;
using EventStore.Core.Util;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

public abstract class with_multi_stream_checkpoint_manager<TLogFormat, TStreamId> : IHandle<ClientMessage.ReadStreamEventsBackward> {
	protected readonly SynchronousScheduler _bus = new();
	protected readonly Guid _projectionId = Guid.NewGuid();
	protected readonly string[] _streams = new string[] {"a", "b", "c"};
	protected readonly string _projectionName = "test_projection";

	protected IODispatcher _ioDispatcher;
	protected ProjectionVersion _projectionVersion;
	protected ProjectionConfig _projectionConfig;
	protected PositionTagger _positionTagger;
	protected ProjectionNamesBuilder _namingBuilder;
	protected CoreProjectionCheckpointWriter _coreProjectionCheckpointWriter;
	protected MultiStreamMultiOutputCheckpointManager _checkpointManager;

	private bool _hasRead;

	[OneTimeSetUp]
	public void TestFixtureSetUp() {
		_ioDispatcher = new IODispatcher(_bus, _bus, true);
		_projectionVersion = new ProjectionVersion(3, 1, 2);
		_projectionConfig = new ProjectionConfig(SystemAccounts.System, 10, 1000, 1000, 10, true, true, true,
			false,
			false, 5000, 10, null);
		_positionTagger = new MultiStreamPositionTagger(3, _streams);
		_positionTagger.AdjustTag(CheckpointTag.FromStreamPositions(3,
			new Dictionary<string, long> {{"a", 0}, {"b", 0}, {"c", 0}}));
		_namingBuilder = ProjectionNamesBuilder.CreateForTest("projection");

		IODispatcherTestHelpers.SubscribeIODispatcher(_ioDispatcher, _bus);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);

		_coreProjectionCheckpointWriter = new CoreProjectionCheckpointWriter(
			_namingBuilder.MakeCheckpointStreamName(), _ioDispatcher,
			_projectionVersion, _projectionName);

		_checkpointManager = new MultiStreamMultiOutputCheckpointManager(_bus, _projectionId, _projectionVersion,
			SystemAccounts.System,
			_ioDispatcher, _projectionConfig, _projectionName, _positionTagger, _namingBuilder, true, true, false,
			_coreProjectionCheckpointWriter, Opts.MaxProjectionStateSizeDefault);

		When();
	}

	public abstract void When();

	public virtual void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (message.EventStreamId == _namingBuilder.GetOrderStreamName())
			message.Envelope.ReplyWith(ReadOrderStream(message));
		if (message.EventStreamId == "a")
			message.Envelope.ReplyWith(ReadTestStream(message));
	}

	public ClientMessage.ReadStreamEventsBackwardCompleted ReadOrderStream(
		ClientMessage.ReadStreamEventsBackward message) {
		ResolvedEvent[] events;
		if (!_hasRead) {
			var checkpoint =
				CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"a", 5}, {"b", 5}, {"c", 5}});
			events = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId, "$>",
				"10@a", checkpoint.ToJsonString(new ProjectionVersion(3, 0, 1)));
			_hasRead = true;
		} else {
			events = new ResolvedEvent[0] { };
		}

		return new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId, message.EventStreamId,
			message.FromEventNumber,
			message.MaxCount, ReadStreamResult.Success, events, null, true, "",
			message.FromEventNumber - events.Length, message.FromEventNumber, true, 10000);
	}

	public ClientMessage.ReadStreamEventsBackwardCompleted ReadTestStream(
		ClientMessage.ReadStreamEventsBackward message) {
		var events =
			IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId, "testevent", "{ \"data\":1 }");
		return new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId, message.EventStreamId,
			message.FromEventNumber,
			message.MaxCount, ReadStreamResult.Success, events, null, true, "", message.FromEventNumber - 1,
			message.FromEventNumber, true, 10000);
	}
}
