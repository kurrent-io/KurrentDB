// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.managed_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_a_managed_projection_without_follower_projections<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private new ITimeProvider _timeProvider;

	private ManagedProjection _mp;
	private Guid _coreProjectionId;
	private string _projectionName;

	[SetUp]
	public new void SetUp() {
		WhenLoop();
	}

	protected override ManualQueue GiveInputQueue() {
		return new ManualQueue(_bus, _timeProvider);
	}

	protected override void Given() {
		_projectionName = "projection";
		_coreProjectionId = Guid.NewGuid();
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
					_bus), _ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
	}

	protected override IEnumerable<WhenStep> When() {
		ProjectionManagementMessage.Command.Post message = new ProjectionManagementMessage.Command.Post(
			Envelope, ProjectionMode.Transient, _projectionName, ProjectionManagementMessage.RunAs.System,
			typeof(FakeForeachStreamProjection), "", true, false, false, false);
		_mp.InitializeNew(
			new ManagedProjection.PersistedState {
				Enabled = message.Enabled,
				HandlerType = message.HandlerType,
				Query = message.Query,
				Mode = message.Mode,
				EmitEnabled = message.EmitEnabled,
				CheckpointsDisabled = !message.CheckpointsEnabled,
				Epoch = -1,
				Version = -1,
				RunAs = message.EnableRunAs ? SerializedRunAs.SerializePrincipal(message.RunAs) : null,
			},
			null);

		var sourceDefinition = new FakeForeachStreamProjection("", Console.WriteLine).GetSourceDefinition();
		var projectionSourceDefinition = ProjectionSourceDefinition.From(sourceDefinition);

		_mp.Handle(
			new CoreProjectionStatusMessage.Prepared(
				_coreProjectionId, projectionSourceDefinition));
		yield break;
	}

	[Test]
	public void publishes_start_message() {
		var startMessage = HandledMessages.OfType<CoreProjectionManagementMessage.Start>().LastOrDefault();
		Assert.IsNotNull(startMessage);
	}
}
