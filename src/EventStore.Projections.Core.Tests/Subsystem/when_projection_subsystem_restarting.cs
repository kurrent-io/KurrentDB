// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
// ReSharper disable InconsistentNaming

namespace EventStore.Projections.Core.Tests.Subsystem;


[TestFixture]
public class when_projection_subsystem_restarted
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;
	private Message _restartResponse;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMsg = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _restartResponse = message)));
	}

	[Test]
	public void should_respond_that_subsystem_is_restarting() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.SubsystemRestarting>(_restartResponse);
	}

	[Test]
	public void should_stop_the_subsystem() {
		var stopMsg = WaitForStopMessage();
		Assert.AreEqual(_instanceCorrelation, stopMsg.InstanceCorrelationId);
	}

	[Test]
	public void should_start_the_subsystem_when_fully_stopped() {
		WaitForStopMessage();

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));

		var restartMsg = WaitForStartMessage("Timed out waiting for restart StartComponents");

		Assert.AreNotEqual(_instanceCorrelation, restartMsg.InstanceCorrelationId);
	}
}

[TestFixture,Explicit]
public class when_projection_subsystem_restarted_twice
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;
	private Guid _restartInstanceCorrelation;

	private Message _secondRestartResponse;

	protected override void Given() {
		// Start subsystem
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		 var startMsg = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		// Restart subsystem
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(new NoopEnvelope()));

		WaitForStopMessage("Timed out waiting for StopComponents on first restart");
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));

		var restartMsg = WaitForStartMessage("Timed out waiting for StartComponents on first restart");
		ResetMessageEvents();
		_restartInstanceCorrelation = restartMsg.InstanceCorrelationId;
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _restartInstanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _restartInstanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));

		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _secondRestartResponse = message)));
	}

	[Test]
	public void should_respond_success_on_second_restart() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.SubsystemRestarting>(_secondRestartResponse);
	}

	[Test]
	public void should_stop_the_subsystem() {
		var stopMsg = WaitForStopMessage("Timed out waiting for StopComponents on second restart");
		Assert.AreEqual(_restartInstanceCorrelation, stopMsg.InstanceCorrelationId);
	}

	[Test]
	public void should_start_the_subsystem() {
		WaitForStopMessage("Timed out waiting for StopComponents on second restart");
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _restartInstanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _restartInstanceCorrelation));

		var restartMsg = WaitForStartMessage("Timed out waiting for StartComponents on second restart");
		Assert.AreNotEqual(_restartInstanceCorrelation, restartMsg.InstanceCorrelationId);
	}
}

[TestFixture]
public class when_projection_subsystem_restarting_and_node_becomes_unknown
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;
	private ProjectionSubsystemMessage.StopComponents _stopMsg;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMsg = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));

		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(new NoopEnvelope()));

		_stopMsg = WaitForStopMessage();

		// Become unknown before components stopped
		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStopped(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));
	}

	[Test]
	public void should_stop_the_subsystem_for_the_restart() {
		Assert.AreEqual(_instanceCorrelation, _stopMsg.InstanceCorrelationId);
	}

	[Test]
	public void should_not_start_the_subsystem() {
		var startMsg = WaitForStartMessage(failOnTimeout: false);
		Assert.IsNull(startMsg);
	}
}

[TestFixture]
public class when_projection_subsystem_starting_and_told_to_restart
	: TestFixtureWithProjectionSubsystem {
	private Message _restartResponse;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		WaitForStartMessage();

		// Restart subsystem before fully started
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _restartResponse = message)));
	}

	[Test]
	public void should_report_restart_failure() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.InvalidSubsystemRestart>(_restartResponse);
	}
}

[TestFixture]
public class when_projection_subsystem_stopping_and_told_to_restart
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;
	private Message _restartResponse;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMsg = WaitForStartMessage();
		ResetMessageEvents();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));

		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

		WaitForStopMessage();

		// Restart subsystem before fully stopped
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _restartResponse = message)));
	}

	[Test]
	public void should_report_restart_failure() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.InvalidSubsystemRestart>(_restartResponse);
	}
}

[TestFixture]
public class when_projection_subsystem_restarting_and_told_to_restart
	: TestFixtureWithProjectionSubsystem {
	private Guid _instanceCorrelation;
	private Message _restartResponse;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		Subsystem.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var startMsg = WaitForStartMessage();
		_instanceCorrelation = startMsg.InstanceCorrelationId;

		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionManager.ServiceName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.ComponentStarted(
			ProjectionCoreCoordinator.ComponentName, _instanceCorrelation));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionManager.ServiceName));
		Subsystem.Handle(new ProjectionSubsystemMessage.IODispatcherDrained(ProjectionCoreService.SubComponentName));

		// First restart
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(new NoopEnvelope()));

		WaitForStopMessage();

		// Restart subsystem before finished restart
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _restartResponse = message)));
	}

	[Test]
	public void should_report_restart_failure() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.InvalidSubsystemRestart>(_restartResponse);
	}
}

[TestFixture]
public class when_projection_subsystem_stopped_and_told_to_restart
	: TestFixtureWithProjectionSubsystem {
	private Message _restartResponse;

	protected override void Given() {
		Subsystem.Handle(new SystemMessage.SystemCoreReady());
		// Don't start the subsystem
		Subsystem.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));

		// Restart subsystem while stopped
		Subsystem.Handle(new ProjectionSubsystemMessage.RestartSubsystem(
			new CallbackEnvelope(message => _restartResponse = message)));
	}

	[Test]
	public void should_report_restart_failure() {
		Assert.IsInstanceOf<ProjectionSubsystemMessage.InvalidSubsystemRestart>(_restartResponse);
	}
}
