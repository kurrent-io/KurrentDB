// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.managed_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_loading_existing_projection_state_with_no_projection_subsystem_version<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private new ITimeProvider _timeProvider;
	private readonly string _projectionName = Guid.NewGuid().ToString();

	private ManagedProjection _mp;

	protected override void Given() {
		var persistedState = new ManagedProjection.PersistedState {
			Enabled = true,
			HandlerType = "JS",
			Query = "log(1);",
			Mode = ProjectionMode.Continuous,
			EmitEnabled = true,
			CheckpointsDisabled = true,
			Epoch = -1,
			Version = -1,
			RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous)
		};
		ExistingEvent($"{ProjectionNamesBuilder.ProjectionsStreamPrefix}{_projectionName}", ProjectionEventTypes.ProjectionUpdated, "",
			persistedState.ToJson());

		_timeProvider = new FakeTimeProvider();
		_mp = new ManagedProjection(
			Guid.NewGuid(),
			Guid.NewGuid(),
			1,
			"name",
			true,
			null,
			_streamDispatcher,
			_writeDispatcher,
			_readDispatcher,
			_bus,
			_timeProvider,
			new(_bus, v => v.CorrelationId, v => v.CorrelationId, _bus),
			new(_bus, v => v.CorrelationId, v => v.CorrelationId, _bus),
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
	}

	[Test]
	public void content_type_validation_is_disabled() {
		_mp.InitializeExisting(_projectionName);
		Assert.False(_mp.EnableContentTypeValidation);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_loading_existing_projection_state_with_projection_subsystem_version<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private new ITimeProvider _timeProvider;
	private readonly string _projectionName = Guid.NewGuid().ToString();

	private ManagedProjection _mp;

	protected override void Given() {
		var persistedState = new ManagedProjection.PersistedState {
			Enabled = true,
			HandlerType = "JS",
			Query = "log(1);",
			Mode = ProjectionMode.Continuous,
			EmitEnabled = true,
			CheckpointsDisabled = true,
			Epoch = -1,
			Version = -1,
			RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
			ProjectionSubsystemVersion = 4
		};
		ExistingEvent($"{ProjectionNamesBuilder.ProjectionsStreamPrefix}{_projectionName}", ProjectionEventTypes.ProjectionUpdated, "",
			persistedState.ToJson());

		_timeProvider = new FakeTimeProvider();
		_mp = new ManagedProjection(
			Guid.NewGuid(),
			Guid.NewGuid(),
			1,
			"name",
			true,
			null,
			_streamDispatcher,
			_writeDispatcher,
			_readDispatcher,
			_bus,
			_timeProvider,
			new(_bus, v => v.CorrelationId, v => v.CorrelationId, _bus),
			new(_bus, v => v.CorrelationId, v => v.CorrelationId, _bus),
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
	}

	[Test]
	public void content_type_validation_is_enabled() {
		_mp.InitializeExisting(_projectionName);
		Assert.True(_mp.EnableContentTypeValidation);
	}
}
