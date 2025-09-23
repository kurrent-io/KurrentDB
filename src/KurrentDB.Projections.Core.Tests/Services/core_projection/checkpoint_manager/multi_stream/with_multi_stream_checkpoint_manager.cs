// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests.Helpers.IODispatcherTests;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

public abstract class with_multi_stream_checkpoint_manager<TLogFormat, TStreamId> : IHandle<ClientMessage.ReadStreamEventsBackward> {
	protected readonly SynchronousScheduler Bus = new();
	protected readonly Guid ProjectionId = Guid.NewGuid();
	protected readonly string[] Streams = ["a", "b", "c"];
	protected readonly string ProjectionName = "test_projection";

	protected IODispatcher IODispatcher;
	protected ProjectionVersion ProjectionVersion;
	protected ProjectionConfig ProjectionConfig;
	protected PositionTagger PositionTagger;
	protected ProjectionNamesBuilder NamingBuilder;
	protected CoreProjectionCheckpointWriter CoreProjectionCheckpointWriter;
	protected MultiStreamMultiOutputCheckpointManager CheckpointManager;

	private bool _hasRead;

	[OneTimeSetUp]
	public void TestFixtureSetUp() {
		IODispatcher = new(Bus, Bus, true);
		ProjectionVersion = new(3, 1, 2);
		ProjectionConfig = new(SystemAccounts.System, 10, 1000, 1000, 10, true, true, false, false, 5000, 10, null);
		PositionTagger = new MultiStreamPositionTagger(3, Streams);
		PositionTagger.AdjustTag(CheckpointTag.FromStreamPositions(3,
			new Dictionary<string, long> { { "a", 0 }, { "b", 0 }, { "c", 0 } }));
		NamingBuilder = ProjectionNamesBuilder.CreateForTest("projection");

		IODispatcherTestHelpers.SubscribeIODispatcher(IODispatcher, Bus);
		Bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);

		CoreProjectionCheckpointWriter = new CoreProjectionCheckpointWriter(
			NamingBuilder.MakeCheckpointStreamName(), IODispatcher,
			ProjectionVersion, ProjectionName);

		CheckpointManager = new MultiStreamMultiOutputCheckpointManager(Bus, ProjectionId, ProjectionVersion,
			SystemAccounts.System,
			IODispatcher, ProjectionConfig, ProjectionName, PositionTagger, NamingBuilder, true,
			CoreProjectionCheckpointWriter, Opts.MaxProjectionStateSizeDefault);

		When();
	}

	protected abstract void When();

	public virtual void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (message.EventStreamId == NamingBuilder.GetOrderStreamName())
			message.Envelope.ReplyWith(ReadOrderStream(message));
		if (message.EventStreamId == "a")
			message.Envelope.ReplyWith(ReadTestStream(message));
	}

	public ClientMessage.ReadStreamEventsBackwardCompleted ReadOrderStream(
		ClientMessage.ReadStreamEventsBackward message) {
		ResolvedEvent[] events;
		if (!_hasRead) {
			var checkpoint = CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> { { "a", 5 }, { "b", 5 }, { "c", 5 } });
			events = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId, "$>",
				"10@a", checkpoint.ToJsonString(new ProjectionVersion(3, 0, 1)));
			_hasRead = true;
		} else {
			events = [];
		}

		return new(message.CorrelationId, message.EventStreamId,
			message.FromEventNumber,
			message.MaxCount, ReadStreamResult.Success, events, null, true, "",
			message.FromEventNumber - events.Length, message.FromEventNumber, true, 10000);
	}

	private static ClientMessage.ReadStreamEventsBackwardCompleted ReadTestStream(ClientMessage.ReadStreamEventsBackward message) {
		var events = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId, "testevent",
			"{ \"data\":1 }");
		return new(message.CorrelationId, message.EventStreamId,
			message.FromEventNumber,
			message.MaxCount, ReadStreamResult.Success, events, null, true, "", message.FromEventNumber - 1,
			message.FromEventNumber, true, 10000);
	}
}
