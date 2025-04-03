// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Tests;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.managed_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.CommitTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.CommitTimeout)]
[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.ForwardTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.ForwardTimeout)]
[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.PrepareTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.PrepareTimeout)]
public class when_persisted_state_write_fails<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private new ITimeProvider _timeProvider;
	private ManagedProjection _managedProjection;
	private Guid _coreProjectionId;
	private string _projectionName;
	private string _projectionDefinitionStreamId;
	private Guid _originalPersistedStateEventId;

	private OperationResult _failureCondition;

	public when_persisted_state_write_fails(OperationResult failureCondition) {
		_failureCondition = failureCondition;
	}

	protected override ManualQueue GiveInputQueue() {
		return new ManualQueue(_bus, _timeProvider);
	}

	[SetUp]
	public void SetUp() {
		AllWritesQueueUp();
		WhenLoop();
	}

	protected override void Given() {
		_projectionName = "projectionName";
		_projectionDefinitionStreamId = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		_coreProjectionId = Guid.NewGuid();
		_timeProvider = new FakeTimeProvider();
		_managedProjection = new ManagedProjection(
			Guid.NewGuid(),
			Guid.NewGuid(),
			1,
			_projectionName,
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
					_bus),
			new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>(
					_bus,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_bus),
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
	}

	protected override IEnumerable<WhenStep> When() {
		ProjectionManagementMessage.Command.Post message = new ProjectionManagementMessage.Command.Post(
			Envelope, ProjectionMode.OneTime, _projectionName, ProjectionManagementMessage.RunAs.System,
			typeof(FakeForeachStreamProjection), "", true, false, false, false);
		_managedProjection.InitializeNew(
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

		_managedProjection.Handle(
			new CoreProjectionStatusMessage.Prepared(
				_coreProjectionId, projectionSourceDefinition));

		_originalPersistedStateEventId = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == _projectionDefinitionStreamId).First().Events[0].EventId;

		CompleteWriteWithResult(_failureCondition);

		_consumer.HandledMessages.Clear();

		yield break;
	}

	[Test]
	public void should_retry_writing_the_persisted_state_with_the_same_event_id() {
		var eventId = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == _projectionDefinitionStreamId).First().Events[0].EventId;
		Assert.AreEqual(eventId, _originalPersistedStateEventId);
	}
}
