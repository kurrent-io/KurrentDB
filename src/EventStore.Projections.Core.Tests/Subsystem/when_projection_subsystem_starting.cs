// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Subsystem;

[TestFixture]
public class when_projection_subsystem_starting_and_all_components_started
	: TestFixtureWithProjectionSubsystem {
	private readonly ManualResetEventSlim _initializedReceived = new ManualResetEventSlim();

	protected override void Given() {
		Subsystem.LeaderOutputBus.Subscribe(
			new AdHocHandler<ProjectionSubsystemMessage.ComponentStarted>(msg => {
				_initializedReceived.Set();
			}));

		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
	}

	[Test]
	public void should_publish_all_components_started() {
		if (!_initializedReceived.Wait(WaitTimeoutMs)) {
			Assert.Fail("Timed out waiting for Subsystem Initialized");
		}
	}
}

[TestFixture]
public class when_projection_subsystem_starting_and_wrong_components_started
	: TestFixtureWithProjectionSubsystem {
	private readonly ManualResetEventSlim _initializedReceived = new ManualResetEventSlim();

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		WaitForStartMessage();

		var wrongCorrelation = Guid.NewGuid();

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, wrongCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, wrongCorrelation));
	}

	[Test]
	public void should_ignore_component_started_for_incorrect_correlation() {
		Assert.False(Started.Wait(WaitTimeoutMs));
	}
}

[TestFixture]
public class when_projection_subsystem_started_and_leader_changes
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMsg = WaitForStartMessage();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
	}

	[Test]
	public void should_stop_the_subsystem_with_start_correlation() {
		var stopMessage = WaitForStopMessage();
		Assert.NotNull(stopMessage);
		Assert.AreEqual(_instanceCorrelation, stopMessage.InstanceCorrelationId);
	}
}

[TestFixture]
public class when_projection_subsystem_stopping_and_all_components_stopped
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMessage = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMessage.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

		WaitForStopMessage();

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));
	}

	[Test]
	public void should_allow_starting_the_subsystem_again() {
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		var startMessage = WaitForStartMessage();

		Assert.AreNotEqual(_instanceCorrelation, startMessage.InstanceCorrelationId);
	}
}

[TestFixture]
public class when_projection_subsystem_starting_and_node_becomes_unknown
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMessage = WaitForStartMessage();
		_instanceCorrelation = startMessage.InstanceCorrelationId;

		// Become unknown before components started
		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
	}

	[Test]
	public void should_stop_the_subsystem() {
		var stopMessage = WaitForStopMessage();
		Assert.AreEqual(_instanceCorrelation, stopMessage.InstanceCorrelationId);
	}
}

[TestFixture]
public class when_projection_subsystem_stopping_and_node_becomes_leader
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMessage = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMessage.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		// Become unknown to stop the subsystem
		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		WaitForStopMessage();

		// Become leader again before subsystem fully stopped
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));
	}

	[Test]
	public void should_start_the_subsystem_again() {
		var startMessages = WaitForStartMessage();
		Assert.AreNotEqual(_instanceCorrelation, startMessages.InstanceCorrelationId);
	}
}
