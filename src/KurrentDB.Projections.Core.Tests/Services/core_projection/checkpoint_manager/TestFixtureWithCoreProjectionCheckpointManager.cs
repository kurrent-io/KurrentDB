// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.SingleStream;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

public abstract class TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected DefaultCheckpointManager _manager;
	protected FakeCoreProjection _projection;
	protected ProjectionConfig _config;
	protected int _checkpointHandledThreshold;
	protected int _checkpointUnhandledBytesThreshold;
	protected int _pendingEventsThreshold;
	protected int _maxWriteBatchLength;
	protected bool _emitEventEnabled;
	protected bool _checkpointsEnabled;
	protected bool _trackEmittedStreams;
	protected int _checkpointAfterMs;
	protected int _maximumAllowedWritesInFlight;
	protected bool _producesResults;
	protected bool _definesFold = true;
	protected Guid _projectionCorrelationId;
	private string _projectionCheckpointStreamId;
	protected bool _createTempStreams;
	protected bool _stopOnEof;
	protected ProjectionNamesBuilder _namingBuilder;
	protected CoreProjectionCheckpointWriter _checkpointWriter;
	protected CoreProjectionCheckpointReader _checkpointReader;
	protected string _projectionName;
	protected ProjectionVersion _projectionVersion;
	protected int _maxProjectionStateSize = Opts.MaxProjectionStateSizeDefault;

	[SetUp]
	public void setup() {
		Given();
		_namingBuilder = ProjectionNamesBuilder.CreateForTest("projection");
		_config = new ProjectionConfig(null, _checkpointHandledThreshold, _checkpointUnhandledBytesThreshold,
			_pendingEventsThreshold, _maxWriteBatchLength, _emitEventEnabled,
			_checkpointsEnabled, _stopOnEof, _trackEmittedStreams, _checkpointAfterMs,
			_maximumAllowedWritesInFlight, null);
		When();
	}

	protected new virtual void When() {
		_projectionVersion = new(1, 0, 0);
		_projectionName = "projection";
		_checkpointWriter = new(_namingBuilder.MakeCheckpointStreamName(), _ioDispatcher, _projectionVersion, _projectionName);
		_checkpointReader = new(GetInputQueue(), _projectionCorrelationId, _ioDispatcher, _projectionCheckpointStreamId,
			_projectionVersion, _checkpointsEnabled);
		_manager = GivenCheckpointManager();
	}

	protected virtual DefaultCheckpointManager GivenCheckpointManager() {
		return new(_bus, _projectionCorrelationId, _projectionVersion, null, _ioDispatcher, _config, _projectionName,
			new StreamPositionTagger(0, "stream"), _namingBuilder, _checkpointsEnabled,
			_checkpointWriter, _maxProjectionStateSize);
	}

	protected new virtual void Given() {
		_projectionCheckpointStreamId = "$projections-projection-checkpoint";
		_projectionCorrelationId = Guid.NewGuid();
		_projection = new FakeCoreProjection();
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointCompleted>(_projection);
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(_projection);
		_bus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(_projection);
		_bus.Subscribe<CoreProjectionProcessingMessage.RestartRequested>(_projection);
		_bus.Subscribe<CoreProjectionProcessingMessage.Failed>(_projection);
		_bus.Subscribe<EventReaderSubscriptionMessage.ReaderAssignedReader>(_projection);

		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionDeleted>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
		_checkpointHandledThreshold = 2;
		_checkpointUnhandledBytesThreshold = 5;
		_pendingEventsThreshold = 5;
		_maxWriteBatchLength = 5;
		_maximumAllowedWritesInFlight = 1;
		_emitEventEnabled = true;
		_checkpointsEnabled = true;
		_producesResults = true;
		_definesFold = true;
		_createTempStreams = false;
		_stopOnEof = false;
		NoStream(_projectionCheckpointStreamId);
	}
}
