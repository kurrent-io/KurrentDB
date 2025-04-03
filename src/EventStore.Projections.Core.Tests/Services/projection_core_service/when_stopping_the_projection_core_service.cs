// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_core_service;

[TestFixture]
public class when_stopping_the_projection_core_service_with_no_running_projections
	: TestFixtureWithProjectionCoreService {
	private readonly Guid _stopCorrelationId = Guid.NewGuid();

	[SetUp]
	public override void Setup() {
		base.Setup();
		_service.Handle(new ProjectionCoreServiceMessage.StopCore(_stopCorrelationId));
	}

	[Test]
	public void should_handle_subcomponent_stopped() {
		var componentStopped = _consumer.HandledMessages
			.OfType<ProjectionCoreServiceMessage.SubComponentStopped>()
			.LastOrDefault(x => x.SubComponent == "ProjectionCoreService");
		Assert.IsNotNull(componentStopped);
		Assert.AreEqual(_stopCorrelationId, componentStopped.QueueId);
	}
}

[TestFixture]
public class when_stopping_the_projection_core_service_with_running_projections
	: TestFixtureWithProjectionCoreService  {
	private readonly Guid _projectionId = Guid.NewGuid();
	private readonly Guid _stopCorrelationId = Guid.NewGuid();

	[SetUp]
	public override void Setup() {
		base.Setup();
		_bus.Subscribe<CoreProjectionStatusMessage.Suspended>(_service);
		_service.Handle(new CoreProjectionManagementMessage.CreateAndPrepare(
			_projectionId, _workerId, "test-projection",
			new ProjectionVersion(), new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false, true, 10000,
				1, 250),
			"JS", "fromStream('$user-admin').outputState()", true));
		_service.Handle(new ProjectionCoreServiceMessage.StopCore(_stopCorrelationId));
	}

	[Test]
	public void should_handle_projection_suspended_message() {
		var suspended = _consumer.HandledMessages
			.OfType<CoreProjectionStatusMessage.Suspended>()
			.LastOrDefault(x => x.ProjectionId == _projectionId);
		Assert.IsNotNull(suspended);
	}

	[Test]
	public void should_handle_subcomponent_stopped() {
		var componentStopped = _consumer.HandledMessages
			.OfType<ProjectionCoreServiceMessage.SubComponentStopped>()
			.LastOrDefault(x => x.SubComponent == "ProjectionCoreService");
		Assert.IsNotNull(componentStopped);
	}
}

[TestFixture]
public class when_stopping_the_projection_core_service_times_out_suspending_projections
	: TestFixtureWithProjectionCoreService  {
	private readonly Guid _projectionId = Guid.NewGuid();
	private readonly Guid _stopCorrelationId = Guid.NewGuid();

	[SetUp]
	public override void Setup() {
		base.Setup();
		// Don't subscribe to the suspended message
		_bus.Unsubscribe<CoreProjectionStatusMessage.Suspended>(_service);
		_service.Handle(new CoreProjectionManagementMessage.CreateAndPrepare(
			_projectionId, _workerId, "test-projection",
			new ProjectionVersion(), new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false, true, 10000,
				1, 250),
			"JS", "fromStream('$user-admin').outputState()", true));
		_service.Handle(new ProjectionCoreServiceMessage.StopCore(_stopCorrelationId));
		_service.Handle(new ProjectionCoreServiceMessage.StopCoreTimeout(_stopCorrelationId));
	}

	[Test]
	public void should_handle_subcomponent_stopped() {
		var componentStopped = _consumer.HandledMessages
			.OfType<ProjectionCoreServiceMessage.SubComponentStopped>()
			.LastOrDefault(x => x.SubComponent == "ProjectionCoreService");
		Assert.IsNotNull(componentStopped);
	}
}

[TestFixture]
public class when_stopping_the_projection_core_service_and_timeout_for_wrong_correlation_received
	: TestFixtureWithProjectionCoreService  {
	private readonly Guid _projectionId = Guid.NewGuid();
	private readonly Guid _stopCorrelationId = Guid.NewGuid();

	[SetUp]
	public override void Setup() {
		base.Setup();
		// Don't subscribe to the suspended message
		_bus.Unsubscribe<CoreProjectionStatusMessage.Suspended>(_service);
		_service.Handle(new CoreProjectionManagementMessage.CreateAndPrepare(
			_projectionId, _workerId, "test-projection",
			new ProjectionVersion(), new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false, true, 10000,
				1, 250),
			"JS", "fromStream('$user-admin').outputState()", true));
		_service.Handle(new ProjectionCoreServiceMessage.StopCore(_stopCorrelationId));
		_service.Handle(new ProjectionCoreServiceMessage.StopCoreTimeout(Guid.NewGuid()));
	}

	[Test]
	public void should_not_handle_subcomponent_stopped() {
		var componentStopped = _consumer.HandledMessages
			.OfType<ProjectionCoreServiceMessage.SubComponentStopped>()
			.LastOrDefault(x => x.SubComponent == "ProjectionCoreService");
		Assert.IsNull(componentStopped);
	}
}
