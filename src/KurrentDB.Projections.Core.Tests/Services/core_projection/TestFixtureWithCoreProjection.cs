// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests.Bus.Helpers;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;
using ClientMessageWriteEvents = KurrentDB.Core.Tests.TestAdapters.ClientMessage.WriteEvents;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithCoreProjection<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected CoreProjection _coreProjection;
	protected TestHandler<ReaderSubscriptionManagement.Subscribe> _subscribeProjectionHandler;
	protected TestHandlerAndConverter<ClientMessage.WriteEvents, ClientMessageWriteEvents> _writeEventHandler;
	protected Guid _firstWriteCorrelationId;
	protected FakeProjectionStateHandler _stateHandler;
	protected int _checkpointHandledThreshold = 5;
	protected int _checkpointUnhandledBytesThreshold = 10000;
	protected Action<SourceDefinitionBuilder> _configureBuilderByQuerySource = null;
	protected Guid _projectionCorrelationId;
	protected ProjectionConfig _projectionConfig;
	protected ProjectionVersion _version;
	protected string _projectionName;
	protected Guid _workerId;

	protected override void Given1() {
		_version = new ProjectionVersion(1, 0, 0);
		_projectionName = "projection";
	}

	[SetUp]
	public void setup() {
		_subscribeProjectionHandler = new();
		_writeEventHandler = new(msg => new ClientMessageWriteEvents(msg));

		_bus.Subscribe(_subscribeProjectionHandler);
		_bus.Subscribe(_writeEventHandler);

		_stateHandler = GivenProjectionStateHandler();
		_firstWriteCorrelationId = Guid.NewGuid();
		_workerId = Guid.NewGuid();
		_projectionCorrelationId = Guid.NewGuid();
		_projectionConfig = GivenProjectionConfig();
		var projectionProcessingStrategy = GivenProjectionProcessingStrategy();
		_coreProjection = GivenCoreProjection(projectionProcessingStrategy);
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointCompleted>(_coreProjection);
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(_coreProjection);
		_bus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(_coreProjection);
		_bus.Subscribe<CoreProjectionProcessingMessage.RestartRequested>(_coreProjection);
		_bus.Subscribe<CoreProjectionProcessingMessage.Failed>(_coreProjection);
		_bus.Subscribe(new AdHocHandler<ProjectionCoreServiceMessage.CoreTick>(tick => tick.Action()));
		PreWhen();
		When();
	}

	protected CoreProjection GivenCoreProjection(ProjectionProcessingStrategy projectionProcessingStrategy) {
		return projectionProcessingStrategy.Create(
			_projectionCorrelationId,
			_bus,
			_bus,
			_ioDispatcher,
			_timeProvider);
	}

	protected virtual ProjectionProcessingStrategy GivenProjectionProcessingStrategy() {
		return CreateProjectionProcessingStrategy();
	}

	private ProjectionProcessingStrategy CreateProjectionProcessingStrategy() {
		return new ContinuousProjectionProcessingStrategy(
			_projectionName, _version, _stateHandler, _projectionConfig, _stateHandler.GetSourceDefinition(), null,
			SubscriptionDispatcher, true, Opts.MaxProjectionStateSizeDefault);
	}

	protected ProjectionProcessingStrategy CreateQueryProcessingStrategy() {
		return new QueryProcessingStrategy(
			_projectionName, _version, _stateHandler, _projectionConfig, _stateHandler.GetSourceDefinition(), null,
			SubscriptionDispatcher, true, Opts.MaxProjectionStateSizeDefault);
	}

	protected ProjectionConfig GivenProjectionConfig() {
		return new ProjectionConfig(
			null, _checkpointHandledThreshold, _checkpointUnhandledBytesThreshold, GivenPendingEventsThreshold(),
			GivenMaxWriteBatchLength(), GivenEmitEventEnabled(), GivenCheckpointsEnabled(),
			GivenStopOnEof(), GivenTrackEmittedStreams(), GivenCheckpointAfterMs(),
			GivenMaximumAllowedWritesInFlight(), null);
	}

	protected int GivenMaxWriteBatchLength() => 250;

	protected virtual int GivenPendingEventsThreshold() => 1000;

	protected virtual bool GivenStopOnEof() => false;

	protected virtual bool GivenCheckpointsEnabled() => true;

	protected bool GivenTrackEmittedStreams() => true;

	protected virtual bool GivenEmitEventEnabled() {
		return true;
	}

	protected int GivenCheckpointAfterMs() => 10000;

	protected int GivenMaximumAllowedWritesInFlight() => 1;

	protected virtual FakeProjectionStateHandler GivenProjectionStateHandler() => new(configureBuilder: _configureBuilderByQuerySource);

	protected new virtual void PreWhen() {
	}

	protected new abstract void When();
}
