// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using Serilog;

namespace EventStore.Projections.Core.Services.Management;

public class ProjectionCoreCoordinator
	: IHandle<ProjectionSubsystemMessage.StartComponents>,
		IHandle<ProjectionSubsystemMessage.StopComponents>,
		IHandle<ProjectionCoreServiceMessage.SubComponentStarted>,
		IHandle<ProjectionCoreServiceMessage.SubComponentStopped> {
	public const string ComponentName = "ProjectionCoreCoordinator";

	private readonly ILogger Log = Serilog.Log.ForContext<ProjectionCoreCoordinator>();
	private readonly ProjectionType _runProjections;

	private readonly Dictionary<Guid, IPublisher> _queues = new Dictionary<Guid, IPublisher>();
	private readonly IPublisher _publisher;

	private int _pendingSubComponentsStarts;
	private int _activeSubComponents;

	private Guid _instanceCorrelationId = Guid.Empty;
	private CoreCoordinatorState _currentState = CoreCoordinatorState.Stopped;

	public ProjectionCoreCoordinator(
		ProjectionType runProjections,
		IReadOnlyList<IPublisher> queues,
		IPublisher publisher){
		_runProjections = runProjections;
		_queues = queues.ToDictionary(_ => Guid.NewGuid(), q => q);
		_publisher = publisher;
	}

	public void Handle(ProjectionSubsystemMessage.StartComponents message) {
		if (_currentState != CoreCoordinatorState.Stopped) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator cannot start components as it's not stopped. Correlation: {correlation}",
				message.InstanceCorrelationId);
			return;
		}
		_instanceCorrelationId = message.InstanceCorrelationId;
		Log.Debug("PROJECTIONS: Projection Core Coordinator component starting. Correlation: {correlation}",
			_instanceCorrelationId);
		Start();
	}

	public void Handle(ProjectionSubsystemMessage.StopComponents message) {
		if (_currentState != CoreCoordinatorState.Started) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator cannot stop components as it's not started. Correlation: {correlation}",
				message.InstanceCorrelationId);
			return;
		}
		if (_instanceCorrelationId != message.InstanceCorrelationId) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator received stop request for incorrect correlation id." +
			          "Current: {correlationId}. Requested: {requestedCorrelationId}", _instanceCorrelationId, message.InstanceCorrelationId);
			return;
		}
		Stop(message);
	}

	private void Start() {
		if (_currentState != CoreCoordinatorState.Stopped) {
			Log.Warning("PROJECTIONS: Projection Core Coordinated tried to start when not stopped.");
			return;
		}
		Log.Debug("PROJECTIONS: Starting Projections Core Coordinator");
		_pendingSubComponentsStarts = 0;
		_activeSubComponents = 0;
		_currentState = CoreCoordinatorState.Starting;

		foreach (var queue in _queues.Values) {
			queue.Publish(new ReaderCoreServiceMessage.StartReader(_instanceCorrelationId));
			_pendingSubComponentsStarts += 1 /*EventReaderCoreService*/;

			if (_runProjections >= ProjectionType.System) {
				queue.Publish(new ProjectionCoreServiceMessage.StartCore(_instanceCorrelationId));
				_pendingSubComponentsStarts += 1; /*ProjectionCoreService*/
			}
		}
	}

	private void Stop(ProjectionSubsystemMessage.StopComponents message) {
		if (_currentState != CoreCoordinatorState.Started) {
			Log.Debug("PROJECTIONS: Projections Core Coordinator trying to stop when not started. " +
			          "Current state: {currentState}. StopCorrelation: {correlation}", _currentState,
				message.InstanceCorrelationId);
			return;
		}

		Log.Debug("PROJECTIONS: Stopping Projections Core Coordinator");
		_currentState = CoreCoordinatorState.Stopping;
		foreach (var queue in _queues) {
			if (_runProjections >= ProjectionType.System) {
				 queue.Value.Publish(new ProjectionCoreServiceMessage.StopCore(queue.Key));
			} else {
				 // TODO: Find out why projections still run even when ProjectionType.None
				 queue.Value.Publish(new ReaderCoreServiceMessage.StopReader(queue.Key));
			}
		}
	}

	public void Handle(ProjectionCoreServiceMessage.SubComponentStarted message) {
		if (_currentState != CoreCoordinatorState.Starting) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator received SubComponent Started when not starting. " +
				"SubComponent: {subComponent}, InstanceCorrelationId: {correlationId}, CurrentState: {currentState}",
				message.SubComponent, message.InstanceCorrelationId, _currentState);
			return;
		}
		if (message.InstanceCorrelationId != _instanceCorrelationId) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator received SubComponent Started for wrong correlation id. " +
				"SubComponent: {subComponent}, RequestedCorrelation: {requestedCorrelation}, InstanceCorrelationId: {correlationId}",
				message.SubComponent, message.InstanceCorrelationId, _instanceCorrelationId);
			return;
		}
		_pendingSubComponentsStarts--;
		_activeSubComponents++;
		Log.Debug("PROJECTIONS: SubComponent Started: {subComponent}", message.SubComponent);

		if (_pendingSubComponentsStarts == 0) {
			_publisher.Publish(
				new ProjectionSubsystemMessage.ComponentStarted(ComponentName, _instanceCorrelationId));
			_currentState = CoreCoordinatorState.Started;
		}
	}

	public void Handle(ProjectionCoreServiceMessage.SubComponentStopped message) {
		if (_currentState != CoreCoordinatorState.Stopping) {
			Log.Debug("PROJECTIONS: Projection Core Coordinator received SubComponent Stopped when not stopping. " +
			          "SubComponent: {subComponent}, CurrentState: {currentState}",
				message.SubComponent, _currentState);
			return;
		}
		_activeSubComponents--;
		Log.Debug("PROJECTIONS: SubComponent Stopped: {subComponent}", message.SubComponent);

		if (message.SubComponent == ProjectionCoreService.SubComponentName) {
			if (!_queues.TryGetValue(message.QueueId, out var queue))
				return;
			queue.Publish(new ReaderCoreServiceMessage.StopReader(message.QueueId));
		}

		if (_activeSubComponents == 0) {
			_publisher.Publish(
				new ProjectionSubsystemMessage.ComponentStopped(ComponentName, _instanceCorrelationId));
			_currentState = CoreCoordinatorState.Stopped;
		}
	}

	public void SetupMessaging(ISubscriber bus) {
		bus.Subscribe<ProjectionCoreServiceMessage.SubComponentStarted>(this);
		bus.Subscribe<ProjectionCoreServiceMessage.SubComponentStopped>(this);
		bus.Subscribe<ProjectionSubsystemMessage.StartComponents>(this);
		bus.Subscribe<ProjectionSubsystemMessage.StopComponents>(this);
	}

	private enum CoreCoordinatorState {
		Stopped,
		Stopping,
		Starting,
		Started
	}
}
