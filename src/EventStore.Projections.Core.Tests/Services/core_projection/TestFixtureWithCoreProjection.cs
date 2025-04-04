// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithCoreProjection<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected CoreProjection _coreProjection;
	protected TestHandler<ReaderSubscriptionManagement.Subscribe> _subscribeProjectionHandler;
	protected TestHandler<ClientMessage.WriteEvents> _writeEventHandler;
	protected Guid _firstWriteCorrelationId;
	protected FakeProjectionStateHandler _stateHandler;
	protected int _checkpointHandledThreshold = 5;
	protected int _checkpointUnhandledBytesThreshold = 10000;
	protected Action<SourceDefinitionBuilder> _configureBuilderByQuerySource = null;
	protected Guid _projectionCorrelationId;
	private bool _createTempStreams = false;
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
		_subscribeProjectionHandler = new TestHandler<ReaderSubscriptionManagement.Subscribe>();
		_writeEventHandler = new TestHandler<ClientMessage.WriteEvents>();
		_bus.Subscribe(_subscribeProjectionHandler);
		_bus.Subscribe(_writeEventHandler);


		_stateHandler = GivenProjectionStateHandler();
		_firstWriteCorrelationId = Guid.NewGuid();
		_workerId = Guid.NewGuid();
		var dispatcher = new ProjectionManagerMessageDispatcher(new Dictionary<Guid, IPublisher>
			{{_workerId, GetInputQueue()}});
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

	protected virtual CoreProjection
		GivenCoreProjection(ProjectionProcessingStrategy projectionProcessingStrategy) {
		return projectionProcessingStrategy.Create(
			_projectionCorrelationId,
			_bus,
			_workerId,
			SystemAccounts.System,
			_bus,
			_ioDispatcher,
			_subscriptionDispatcher,
			_timeProvider);
	}

	protected virtual ProjectionProcessingStrategy GivenProjectionProcessingStrategy() {
		return CreateProjectionProcessingStrategy();
	}

	protected ProjectionProcessingStrategy CreateProjectionProcessingStrategy() {
		return new ContinuousProjectionProcessingStrategy(
			_projectionName, _version, _stateHandler, _projectionConfig, _stateHandler.GetSourceDefinition(), null,
			_subscriptionDispatcher, true, Opts.MaxProjectionStateSizeDefault);
	}

	protected ProjectionProcessingStrategy CreateQueryProcessingStrategy() {
		return new QueryProcessingStrategy(
			_projectionName, _version, _stateHandler, _projectionConfig, _stateHandler.GetSourceDefinition(), null,
			_subscriptionDispatcher, true, Opts.MaxProjectionStateSizeDefault);
	}

	protected virtual ProjectionConfig GivenProjectionConfig() {
		return new ProjectionConfig(
			null, _checkpointHandledThreshold, _checkpointUnhandledBytesThreshold, GivenPendingEventsThreshold(),
			GivenMaxWriteBatchLength(), GivenEmitEventEnabled(), GivenCheckpointsEnabled(), _createTempStreams,
			GivenStopOnEof(), GivenTrackEmittedStreams(), GivenCheckpointAfterMs(),
			GivenMaximumAllowedWritesInFlight(), null);
	}

	protected virtual int GivenMaxWriteBatchLength() {
		return 250;
	}

	protected virtual int GivenPendingEventsThreshold() {
		return 1000;
	}

	protected virtual bool GivenStopOnEof() {
		return false;
	}

	protected virtual bool GivenCheckpointsEnabled() {
		return true;
	}

	protected virtual bool GivenTrackEmittedStreams() {
		return true;
	}

	protected virtual bool GivenEmitEventEnabled() {
		return true;
	}

	protected virtual int GivenCheckpointAfterMs() {
		return 10000;
	}

	protected virtual int GivenMaximumAllowedWritesInFlight() {
		return 1;
	}

	protected virtual FakeProjectionStateHandler GivenProjectionStateHandler() {
		return new FakeProjectionStateHandler(configureBuilder: _configureBuilderByQuerySource);
	}

	protected new virtual void PreWhen() {
	}

	protected new abstract void When();
}
