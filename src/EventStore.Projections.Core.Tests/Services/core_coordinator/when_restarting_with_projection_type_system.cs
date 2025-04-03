// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Management;
using EventStore.Common.Options;
using EventStore.Projections.Core.Messages;
using EventStore.Core.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_restarting_with_projection_type_system {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;
	private Guid instanceCorrelationId = Guid.NewGuid();
	private Guid queueId;

	[SetUp]
	public void Setup() {
		queues = new List<FakePublisher>() {new FakePublisher()}.ToArray();
		publisher = new FakePublisher();

		_coordinator =
			new ProjectionCoreCoordinator(ProjectionType.System, queues, publisher);

		// Start components and handle started
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
				instanceCorrelationId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(ProjectionCoreService.SubComponentName,
				instanceCorrelationId));

		// Stop components but don't handle stopped
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));

		var stopCore = queues[0].Messages.OfType<ProjectionCoreServiceMessage.StopCore>().First();
		queueId = stopCore.QueueId;
		//clear queues for clearer testing
		queues[0].Messages.Clear();
	}

	[Test]
	public void should_not_start_if_subcomponents_not_stopped() {
		// None of the subcomponents stopped

		// Start Components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StartCore).Count);
	}

	[Test]
	public void should_not_start_if_not_all_subcomponents_stopped() {
		// Not all components stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));

		// Start Components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StartCore).Count);
	}

	[Test]
	public void should_start_if_subcomponents_stopped_before_starting_components_again() {
		// All components stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
				queueId));

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));

		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StartCore).Count);
	}

	[Test]
	public void should_not_stop_if_all_subcomponents_not_started() {
		var newInstanceCorrelationId = Guid.NewGuid();

		// All components stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
				queueId));

		queues[0].Messages.Clear();

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(newInstanceCorrelationId));

		// Not all subcomponents started
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
				newInstanceCorrelationId));

		// Stop components
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(newInstanceCorrelationId));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StopCore).Count);
	}

	[Test]
	public void should_not_stop_if_not_started() {
		// All components stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
				queueId));

		queues[0].Messages.Clear();

		// Stop components
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StopCore).Count);
	}

	[Test]
	public void should_not_stop_if_correlation_id_is_different() {
		var newInstanceCorrelationId = Guid.NewGuid();

		// All components stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
				queueId));

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(newInstanceCorrelationId));

		// All components started
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
				newInstanceCorrelationId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(ProjectionCoreService.SubComponentName,
				newInstanceCorrelationId));

		queues[0].Messages.Clear();
		// Stop components with a different correlation id
		var incorrectCorrelationId = Guid.NewGuid();
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(incorrectCorrelationId));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ProjectionCoreServiceMessage.StopCore).Count);
	}
}
