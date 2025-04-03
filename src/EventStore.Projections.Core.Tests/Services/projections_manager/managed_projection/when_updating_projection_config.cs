// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.Util;
using EventStore.Projections.Core.Common;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Tests.Services.core_projection;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.managed_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_initializing_projection_with_default_options<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private Guid _projectionId = Guid.NewGuid();
	private ProjectionManagementMessage.ProjectionConfig _config;
	private EventRecord _persistedStateWrite;

	private ManagedProjection.PersistedState _persistedState = new ManagedProjection.PersistedState {
		Enabled = true,
		HandlerType = "JS",
		Query = "fromAll().when({});",
		Mode = ProjectionMode.Continuous,
		CheckpointsDisabled = null,
		Epoch = null,
		Version = null,
		RunAs = null,
		EmitEnabled = null,
		TrackEmittedStreams = null,
		CheckpointAfterMs = (int)ProjectionConsts.CheckpointAfterMs.TotalMilliseconds,
		CheckpointHandledThreshold = ProjectionConsts.CheckpointHandledThreshold,
		CheckpointUnhandledBytesThreshold = ProjectionConsts.CheckpointUnhandledBytesThreshold,
		MaxAllowedWritesInFlight = ProjectionConsts.MaxAllowedWritesInFlight,
		ProjectionExecutionTimeout = null,
		CreateTempStreams = null
	};

	public when_initializing_projection_with_default_options() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();

		_mp.InitializeNew(
			_persistedState,
			null);
		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));

		// Complete write of persisted state to start projection
		OneWriteCompletes();
		_config = GetProjectionConfig(_mp);
		_persistedStateWrite = _streams[ProjectionStreamId].LastOrDefault();
	}

	[Test]
	public void projection_execution_timeout_should_be_null() {
		Assert.IsNotNull(_config);
		Assert.IsNull(_config.ProjectionExecutionTimeout, "ProjectionExecutionTimeout");
	}

	[Test]
	public void emit_options_should_default_to_false() {
		Assert.IsNotNull(_config);
		Assert.AreEqual(false, _config.EmitEnabled, "EmitEnabled");
		Assert.AreEqual(false, _config.TrackEmittedStreams, "TrackEmittedStreams");
	}

	[Test]
	public void persisted_state_should_leave_fallback_options_unset() {
		Assert.IsNotNull(_persistedStateWrite);
		var actualState = _persistedStateWrite.Data.ParseJson<ManagedProjection.PersistedState>();
		Assert.IsNull(actualState.ProjectionExecutionTimeout, "ProjectionExecutionTimeout");
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_initializing_projection_with_persisted_state<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private Guid _projectionId = Guid.NewGuid();
	private ProjectionManagementMessage.ProjectionConfig _config;
	private EventRecord _persistedStateWrite;

	private ManagedProjection.PersistedState _persistedState = new ManagedProjection.PersistedState {
		Enabled = true,
		HandlerType = "JS",
		Query = "fromAll().when({});",
		Mode = ProjectionMode.Continuous,
		CheckpointsDisabled = false,
		Epoch = -1,
		Version = -1,
		RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
		EmitEnabled = false,
		TrackEmittedStreams = true,
		CheckpointAfterMs = 1,
		CheckpointHandledThreshold = 2,
		CheckpointUnhandledBytesThreshold = 3,
		PendingEventsThreshold = 4,
		MaxWriteBatchLength = 5,
		MaxAllowedWritesInFlight = 6,
		ProjectionExecutionTimeout = 11
	};

	public when_initializing_projection_with_persisted_state() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();

		_mp.InitializeNew(
			_persistedState,
			null);
		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));

		// Complete write of persisted state to start projection
		OneWriteCompletes();
		_config = GetProjectionConfig(_mp);
		_persistedStateWrite = _streams[ProjectionStreamId].LastOrDefault();
	}

	[Test]
	public void config_should_be_same_as_persisted_state() {
		Assert.IsNotNull(_config);
		Assert.AreEqual(_persistedState.EmitEnabled, _config.EmitEnabled, "EmitEnabled");
		Assert.AreEqual(_persistedState.TrackEmittedStreams, _config.TrackEmittedStreams, "TrackEmittedStreams");
		Assert.AreEqual(_persistedState.CheckpointAfterMs, _config.CheckpointAfterMs, "CheckpointAfterMs");
		Assert.AreEqual(_persistedState.CheckpointHandledThreshold, _config.CheckpointHandledThreshold,
			"CheckpointHandledThreshold");
		Assert.AreEqual(_persistedState.CheckpointUnhandledBytesThreshold,
			_config.CheckpointUnhandledBytesThreshold, "CheckpointUnhandledBytesThreshold");
		Assert.AreEqual(_persistedState.PendingEventsThreshold, _config.PendingEventsThreshold,
			"PendingEventsThreshold");
		Assert.AreEqual(_persistedState.MaxWriteBatchLength, _config.MaxWriteBatchLength, "MaxWriteBatchLength");
		Assert.AreEqual(_persistedState.MaxAllowedWritesInFlight, _config.MaxAllowedWritesInFlight,
			"MaxAllowedWritesInFlight");
		Assert.AreEqual(_persistedState.ProjectionExecutionTimeout, _config.ProjectionExecutionTimeout,
			"ProjectionExecutionTimeout");
	}

	[Test]
	public void persisted_state_is_written_correctly() {
		Assert.IsNotNull(_persistedStateWrite);
		var actualState = _persistedStateWrite.Data.ParseJson<ManagedProjection.PersistedState>();

		Assert.AreEqual(_persistedState.EmitEnabled, actualState.EmitEnabled, "EmitEnabled");
		Assert.AreEqual(_persistedState.TrackEmittedStreams, actualState.TrackEmittedStreams, "TrackEmittedStreams");
		Assert.AreEqual(_persistedState.CheckpointAfterMs, actualState.CheckpointAfterMs, "CheckpointAfterMs");
		Assert.AreEqual(_persistedState.CheckpointHandledThreshold, actualState.CheckpointHandledThreshold,
			"CheckpointHandledThreshold");
		Assert.AreEqual(_persistedState.CheckpointUnhandledBytesThreshold,
			actualState.CheckpointUnhandledBytesThreshold, "CheckpointUnhandledBytesThreshold");
		Assert.AreEqual(_persistedState.PendingEventsThreshold, actualState.PendingEventsThreshold,
			"PendingEventsThreshold");
		Assert.AreEqual(_persistedState.MaxWriteBatchLength, actualState.MaxWriteBatchLength, "MaxWriteBatchLength");
		Assert.AreEqual(_persistedState.MaxAllowedWritesInFlight, actualState.MaxAllowedWritesInFlight,
			"MaxAllowedWritesInFlight");
		Assert.AreEqual(_persistedState.ProjectionExecutionTimeout, actualState.ProjectionExecutionTimeout,
			"ProjectionExecutionTimeout");
	}
}


[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_updating_projection_config_to_remove_execution_timeout<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private Guid _projectionId = Guid.NewGuid();
	private ProjectionManagementMessage.ProjectionConfig _config;
	private EventRecord _persistedStateWrite;
	private ProjectionManagementMessage.Command.UpdateConfig _updateConfig;

	private ManagedProjection.PersistedState _persistedState => new ManagedProjection.PersistedState {
		Enabled = false,
		HandlerType = "JS",
		Query = "fromAll().when({});",
		Mode = ProjectionMode.Continuous,
		CheckpointsDisabled = false,
		Epoch = -1,
		Version = -1,
		RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
		EmitEnabled = false,
		TrackEmittedStreams = true,
		CheckpointAfterMs = 1,
		CheckpointHandledThreshold = 2,
		CheckpointUnhandledBytesThreshold = 3,
		PendingEventsThreshold = 4,
		MaxWriteBatchLength = 5,
		MaxAllowedWritesInFlight = 6,
		ProjectionExecutionTimeout = 11
	};

	public when_updating_projection_config_to_remove_execution_timeout() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();

		_mp.InitializeNew(
			_persistedState,
			null);
		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));
		OneWriteCompletes();
		_mp.Handle(new CoreProjectionStatusMessage.Stopped(_projectionId, ProjectionName, false));

		_updateConfig = new ProjectionManagementMessage.Command.UpdateConfig(
			new NoopEnvelope(), ProjectionName, _persistedState.EmitEnabled ?? false, _persistedState.TrackEmittedStreams ?? false,
			_persistedState.CheckpointAfterMs, _persistedState.CheckpointHandledThreshold, _persistedState.CheckpointUnhandledBytesThreshold,
			_persistedState.PendingEventsThreshold, _persistedState.MaxWriteBatchLength, _persistedState.MaxAllowedWritesInFlight,
			_persistedState.RunAs,
			projectionExecutionTimeout: null);
		_mp.Handle(_updateConfig);
		OneWriteCompletes();

		_config = GetProjectionConfig(_mp);
		_persistedStateWrite = _streams[ProjectionStreamId].LastOrDefault();

	}

	[Test]
	public void config_should_have_null_projection_execution_timeout() {
		Assert.IsNotNull(_config);
		Assert.IsNull(_config.ProjectionExecutionTimeout, "ProjectionExecutionTimeout");
	}

	[Test]
	public void persisted_state_should_have_null_projection_execution_timeout() {
	Assert.IsNotNull(_persistedStateWrite);
		var actualState = _persistedStateWrite.Data.ParseJson<ManagedProjection.PersistedState>();
		Assert.IsNull(actualState.ProjectionExecutionTimeout, "ProjectionExecutionTimeout");
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_projection_config_of_faulted_projection<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private Guid _projectionId = Guid.NewGuid();
	private Exception _thrownException;
	private ProjectionManagementMessage.Command.UpdateConfig _updateConfig;

	public when_updating_projection_config_of_faulted_projection() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();
		_mp.InitializeNew(
			new ManagedProjection.PersistedState {
				Enabled = false,
				HandlerType = "JS",
				Query = "fromAll().when({});",
				Mode = ProjectionMode.Continuous,
				EmitEnabled = true,
				CheckpointsDisabled = false,
				Epoch = -1,
				Version = -1,
				RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
				ProjectionExecutionTimeout = 11
			},
			null);

		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));
		OneWriteCompletes();
		_consumer.HandledMessages.Clear();

		_mp.Handle(new CoreProjectionStatusMessage.Faulted(
			_projectionId,
			"test"));

		_updateConfig = CreateConfig();
		try {
			_mp.Handle(_updateConfig);
		} catch (Exception ex) {
			_thrownException = ex;
		}
	}

	[Test]
	public void persisted_state_is_written() {
		var writeEvents = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().ToList();
		Assert.AreEqual(1, writeEvents.Count());
		Assert.AreEqual(ProjectionStreamId, writeEvents[0].EventStreamId);
	}

	[Test]
	public void config_update_does_not_throw_exception() {
		Assert.IsNull(_thrownException);
	}

	[Test]
	public void config_is_updated() {
		var getConfigResult = GetProjectionConfig(_mp);

		Assert.IsNotNull(getConfigResult);
		Assert.AreEqual(_updateConfig.EmitEnabled, getConfigResult.EmitEnabled);
		Assert.AreEqual(_updateConfig.TrackEmittedStreams, getConfigResult.TrackEmittedStreams);
		Assert.AreEqual(_updateConfig.CheckpointAfterMs, getConfigResult.CheckpointAfterMs);
		Assert.AreEqual(_updateConfig.CheckpointHandledThreshold, getConfigResult.CheckpointHandledThreshold);
		Assert.AreEqual(_updateConfig.CheckpointUnhandledBytesThreshold,
			getConfigResult.CheckpointUnhandledBytesThreshold);
		Assert.AreEqual(_updateConfig.PendingEventsThreshold, getConfigResult.PendingEventsThreshold);
		Assert.AreEqual(_updateConfig.MaxWriteBatchLength, getConfigResult.MaxWriteBatchLength);
		Assert.AreEqual(_updateConfig.MaxAllowedWritesInFlight, getConfigResult.MaxAllowedWritesInFlight);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_projection_config_of_running_projection<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private Guid _projectionId = Guid.NewGuid();

	private ManagedProjection.PersistedState _persistedState = new ManagedProjection.PersistedState {
		Enabled = true,
		HandlerType = "JS",
		Query = "fromAll().when({});",
		Mode = ProjectionMode.Continuous,
		CheckpointsDisabled = false,
		Epoch = -1,
		Version = -1,
		RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
		EmitEnabled = false,
		TrackEmittedStreams = true,
		CheckpointAfterMs = 1,
		CheckpointHandledThreshold = 2,
		CheckpointUnhandledBytesThreshold = 3,
		PendingEventsThreshold = 4,
		MaxWriteBatchLength = 5,
		MaxAllowedWritesInFlight = 6,
		ProjectionExecutionTimeout = 11
	};

	private InvalidOperationException _thrownException;

	public when_updating_projection_config_of_running_projection() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();

		_mp.InitializeNew(
			_persistedState,
			null);
		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));

		// Complete write of persisted state to start projection
		OneWriteCompletes();

		try {
			_mp.Handle(CreateConfig());
		} catch (InvalidOperationException ex) {
			_thrownException = ex;
		}
	}

	[Test]
	public void should_throw_exception_when_trying_to_update_config() {
		Assert.IsNotNull(_thrownException);
	}

	[Test]
	public void config_should_remain_unchanged() {
		var getConfigResult = GetProjectionConfig(_mp);

		Assert.IsNotNull(getConfigResult);
		Assert.AreEqual(_persistedState.EmitEnabled, getConfigResult.EmitEnabled, "EmitEnabled");
		Assert.AreEqual(_persistedState.TrackEmittedStreams, getConfigResult.TrackEmittedStreams,
			"TrackEmittedStreams");
		Assert.AreEqual(_persistedState.CheckpointAfterMs, getConfigResult.CheckpointAfterMs, "CheckpointAfterMs");
		Assert.AreEqual(_persistedState.CheckpointHandledThreshold, getConfigResult.CheckpointHandledThreshold,
			"CheckpointHandledThreshold");
		Assert.AreEqual(_persistedState.CheckpointUnhandledBytesThreshold,
			getConfigResult.CheckpointUnhandledBytesThreshold, "CheckpointUnhandledBytesThreshold");
		Assert.AreEqual(_persistedState.PendingEventsThreshold, getConfigResult.PendingEventsThreshold,
			"PendingEventsThreshold");
		Assert.AreEqual(_persistedState.MaxWriteBatchLength, getConfigResult.MaxWriteBatchLength,
			"MaxWriteBatchLength");
		Assert.AreEqual(_persistedState.MaxAllowedWritesInFlight, getConfigResult.MaxAllowedWritesInFlight,
			"MaxAllowedWritesInFlight");
		Assert.AreEqual(_persistedState.ProjectionExecutionTimeout, getConfigResult.ProjectionExecutionTimeout,
			"ProjectionExecutionTimeout");
	}
}

public abstract class projection_config_test_base<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected const string ProjectionName = "name";
	protected readonly string ProjectionStreamId = ProjectionNamesBuilder.ProjectionsStreamPrefix + ProjectionName;
	protected ManagedProjection CreateManagedProjection() {
		return new ManagedProjection(
			Guid.NewGuid(),
			Guid.NewGuid(),
			1,
			ProjectionName,
			true,
			null,
			_streamDispatcher,
			_writeDispatcher,
			_readDispatcher,
			_bus,
			_timeProvider, new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>(
					_bus,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_bus), new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>(
					_bus,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_bus),
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
	}

	protected ProjectionManagementMessage.Command.UpdateConfig CreateConfig() {
		return new ProjectionManagementMessage.Command.UpdateConfig(
			new NoopEnvelope(), ProjectionName, true, false, 100, 200, 300, 400, 500, 600,
			ProjectionManagementMessage.RunAs.Anonymous, ClusterVNodeOptions.ProjectionOptions.DefaultProjectionExecutionTimeout);
	}

	protected ProjectionManagementMessage.ProjectionConfig GetProjectionConfig(ManagedProjection mp) {
		ProjectionManagementMessage.ProjectionConfig getConfigResult = null;
		mp.Handle(new ProjectionManagementMessage.Command.GetConfig(
			new CallbackEnvelope(m => getConfigResult = (ProjectionManagementMessage.ProjectionConfig)m), "name",
			SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous)));
		return getConfigResult;
	}
}
