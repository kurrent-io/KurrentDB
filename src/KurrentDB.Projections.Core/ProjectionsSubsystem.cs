// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using DotNext;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Subsystems;
using EventStore.Projections.Core.Services.Grpc;
using KurrentDB.Common.Configuration;
using KurrentDB.Common.Options;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.AwakeReaderService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core;

public record ProjectionSubsystemOptions(
	int ProjectionWorkerThreadCount,
	ProjectionType RunProjections,
	bool StartStandardProjections,
	TimeSpan ProjectionQueryExpiry,
	bool FaultOutOfOrderProjections,
	int CompilationTimeout,
	int ExecutionTimeout,
	int MaxProjectionStateSize);

public sealed class ProjectionsSubsystem : ISubsystem,
	IHandle<SystemMessage.SystemCoreReady>,
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<CoreProjectionStatusMessage.Stopped>,
	IHandle<CoreProjectionStatusMessage.Started>,
	IHandle<ProjectionSubsystemMessage.RestartSubsystem>,
	IHandle<ProjectionSubsystemMessage.ComponentStarted>,
	IHandle<ProjectionSubsystemMessage.ComponentStopped>,
	IHandle<ProjectionSubsystemMessage.IODispatcherDrained> {

	static readonly ILogger Logger = Log.ForContext<ProjectionsSubsystem>();

	public const int VERSION = 4;
	public const int CONTENT_TYPE_VALIDATION_VERSION = 4;

	private readonly int _projectionWorkerThreadCount;
	private readonly ProjectionType _runProjections;
	private readonly bool _startStandardProjections;
	private readonly TimeSpan _projectionsQueryExpiry;

	private readonly InMemoryBus _leaderInputBus;
	private readonly InMemoryBus _leaderOutputBus;

	private IQueuedHandler _leaderInputQueue;
	private IQueuedHandler _leaderOutputQueue;

	private IDictionary<Guid, CoreWorker> _coreWorkers;
	private Dictionary<Guid, IPublisher> _queueMap;
	private bool _subsystemStarted;
	private readonly TaskCompletionSource _subsystemInitialized;

	private readonly bool _faultOutOfOrderProjections;

	private readonly int _compilationTimeout;
	private readonly int _executionTimeout;
	private readonly int _maxProjectionStateSize;

	private readonly int _componentCount;
	private readonly int _dispatcherCount;
	private bool _restarting;
	private int _pendingComponentStarts;
	private int _runningComponentCount;
	private int _runningDispatchers;

	private VNodeState _nodeState;
	private SubsystemState _subsystemState = SubsystemState.NotReady;
	private Guid _instanceCorrelationId;
	private IProjectionTracker _projectionTracker = IProjectionTracker.NoOp;

	private readonly List<string> _standardProjections = new List<string> {
		"$by_category",
		"$stream_by_category",
		"$streams",
		"$by_event_type",
		"$by_correlation_id"
	};

	public ProjectionsSubsystem(ProjectionSubsystemOptions projectionSubsystemOptions) {
		if (projectionSubsystemOptions.RunProjections <= ProjectionType.System)
			_projectionWorkerThreadCount = 1;
		else
			_projectionWorkerThreadCount = projectionSubsystemOptions.ProjectionWorkerThreadCount;

		_runProjections = projectionSubsystemOptions.RunProjections;
		// Projection manager & Projection Core Coordinator
		// The manager only starts when projections are running
		_componentCount = _runProjections == ProjectionType.None ? 1 : 2;

		// Projection manager & each projection core worker
		_dispatcherCount = 1 + _projectionWorkerThreadCount;

		_startStandardProjections = projectionSubsystemOptions.StartStandardProjections;
		_projectionsQueryExpiry = projectionSubsystemOptions.ProjectionQueryExpiry;
		_faultOutOfOrderProjections = projectionSubsystemOptions.FaultOutOfOrderProjections;

		_leaderInputBus = new InMemoryBus("manager input bus");
		_leaderOutputBus = new InMemoryBus("ProjectionManagerAndCoreCoordinatorOutput");

		_subsystemInitialized = new();
		_executionTimeout = projectionSubsystemOptions.ExecutionTimeout;
		_compilationTimeout = projectionSubsystemOptions.CompilationTimeout;
		_maxProjectionStateSize = projectionSubsystemOptions.MaxProjectionStateSize;
	}

	public IPublisher LeaderOutputQueue => _leaderOutputQueue;
	public IPublisher LeaderInputQueue => _leaderInputQueue;
	public ISubscriber LeaderOutputBus => _leaderOutputBus;
	public ISubscriber LeaderInputBus => _leaderInputBus;

	public string Name => "Projections";
	public string DiagnosticsName => Name;
	public KeyValuePair<string, object>[] DiagnosticsTags => [];
	public string Version => VERSION.ToString();
	public bool Enabled => true;
	public string LicensePublicKey => string.Empty;

	public void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
		var standardComponents = builder.ApplicationServices.GetRequiredService<StandardComponents>();

		_leaderInputQueue = new QueuedHandlerThreadPool(
			_leaderInputBus,
			"Projections Leader",
			standardComponents.QueueStatsManager,
			standardComponents.QueueTrackers
		);
		_leaderOutputQueue = new QueuedHandlerThreadPool(
			_leaderOutputBus,
			"Projections Leader",
			standardComponents.QueueStatsManager,
			standardComponents.QueueTrackers
		);

		LeaderInputBus.Subscribe<ProjectionSubsystemMessage.RestartSubsystem>(this);
		LeaderInputBus.Subscribe<ProjectionSubsystemMessage.ComponentStarted>(this);
		LeaderInputBus.Subscribe<ProjectionSubsystemMessage.ComponentStopped>(this);
		LeaderInputBus.Subscribe<ProjectionSubsystemMessage.IODispatcherDrained>(this);
		LeaderInputBus.Subscribe<SystemMessage.SystemCoreReady>(this);
		LeaderInputBus.Subscribe<SystemMessage.StateChangeMessage>(this);

		var projectionsStandardComponents = new ProjectionsStandardComponents(
			_projectionWorkerThreadCount,
			_runProjections,
			leaderOutputBus: _leaderOutputBus,
			leaderOutputQueue: _leaderOutputQueue,
			leaderInputBus: _leaderInputBus,
			leaderInputQueue: _leaderInputQueue,
			_faultOutOfOrderProjections,
			_compilationTimeout,
			_executionTimeout,
			_maxProjectionStateSize);

		CreateAwakerService(standardComponents);
		_coreWorkers = ProjectionCoreWorkersNode.CreateCoreWorkers(standardComponents, projectionsStandardComponents);
		_queueMap = _coreWorkers.ToDictionary(v => v.Key, v => v.Value.CoreInputQueue.As<IPublisher>());

		ConfigureProjectionMetrics(standardComponents.MetricsConfiguration);

		ProjectionManagerNode.CreateManagerService(standardComponents, projectionsStandardComponents, _queueMap,
			_projectionsQueryExpiry, _projectionTracker);
		LeaderInputBus.Subscribe<CoreProjectionStatusMessage.Stopped>(this);
		LeaderInputBus.Subscribe<CoreProjectionStatusMessage.Started>(this);

		builder.UseEndpoints(endpoints => endpoints.MapGrpcService<ProjectionManagement>());
	}

	private void ConfigureProjectionMetrics(MetricsConfiguration conf) {
		if (!conf.ProjectionStats)
			return;

		var projectionMeter = new Meter(conf.ProjectionsMeterName, version: "1.0.0");
		var serviceName = conf.ServiceName;

		var tracker = new ProjectionTracker();
		_projectionTracker = tracker;

		projectionMeter.CreateObservableCounter($"{serviceName}-projection-events-processed-after-restart-total", tracker.ObserveEventsProcessed);
		projectionMeter.CreateObservableUpDownCounter($"{serviceName}-projection-progress", tracker.ObserveProgress);
		projectionMeter.CreateObservableUpDownCounter($"{serviceName}-projection-running", tracker.ObserveRunning);
		projectionMeter.CreateObservableUpDownCounter($"{serviceName}-projection-status", tracker.ObserveStatus);
		projectionMeter.CreateObservableUpDownCounter($"{serviceName}-projection-state-size", tracker.ObserveStateSize);
	}

	public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
		services.AddSingleton(provider => new ProjectionManagement(_leaderInputQueue, provider.GetRequiredService<IAuthorizationProvider>()));

	private static void CreateAwakerService(StandardComponents standardComponents) {
		var awakeReaderService = new AwakeService();
		standardComponents.MainBus.Subscribe<StorageMessage.EventCommitted>(awakeReaderService);
		standardComponents.MainBus.Subscribe<StorageMessage.TfEofAtNonCommitRecord>(awakeReaderService);
		standardComponents.MainBus.Subscribe<AwakeServiceMessage.SubscribeAwake>(awakeReaderService);
		standardComponents.MainBus.Subscribe<AwakeServiceMessage.UnsubscribeAwake>(awakeReaderService);
	}

	public void Handle(SystemMessage.SystemCoreReady message) {
		if (_subsystemState != SubsystemState.NotReady)
			return;
		_subsystemState = SubsystemState.Ready;
		if (_nodeState == VNodeState.Leader) {
			StartComponents();
			return;
		}
		if (_nodeState == VNodeState.Follower || _nodeState == VNodeState.ReadOnlyReplica) {
			PublishInitialized();
		}
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		_nodeState = message.State;
		if (_subsystemState == SubsystemState.NotReady)
			return;

		if (_nodeState == VNodeState.Leader) {
			StartComponents();
			return;
		}

		if (_nodeState == VNodeState.Follower || _nodeState == VNodeState.ReadOnlyReplica) {
			PublishInitialized();
		}
		StopComponents();
	}

	private void StartComponents() {
		if (_nodeState != VNodeState.Leader) {
			Logger.Debug("PROJECTIONS SUBSYSTEM: Not starting because node is not leader. Current node state: {nodeState}",
				_nodeState);
			return;
		}
		if (_subsystemState != SubsystemState.Ready && _subsystemState != SubsystemState.Stopped) {
			Logger.Debug("PROJECTIONS SUBSYSTEM: Not starting because system is not ready or stopped. Current Subsystem state: {subsystemState}",
				_subsystemState);
			return;
		}
		if (_runningComponentCount > 0) {
			Logger.Warning("PROJECTIONS SUBSYSTEM: Subsystem is stopped, but components are still running.");
			return;
		}

		_subsystemState = SubsystemState.Starting;
		_restarting = false;
		_instanceCorrelationId = Guid.NewGuid();
		Logger.Information("PROJECTIONS SUBSYSTEM: Starting components for Instance: {instanceCorrelationId}", _instanceCorrelationId);
		_pendingComponentStarts = _componentCount;
		LeaderInputQueue.Publish(new ProjectionSubsystemMessage.StartComponents(_instanceCorrelationId));
	}

	private void StopComponents() {
		if (_subsystemState != SubsystemState.Started) {
			Logger.Debug("PROJECTIONS SUBSYSTEM: Not stopping because subsystem is not in a started state. Current Subsystem state: {state}", _subsystemState);
			return;
		}

		Logger.Information("PROJECTIONS SUBSYSTEM: Stopping components for Instance: {instanceCorrelationId}", _instanceCorrelationId);
		_subsystemState = SubsystemState.Stopping;
		LeaderInputQueue.Publish(new ProjectionSubsystemMessage.StopComponents(_instanceCorrelationId));
	}

	public void Handle(ProjectionSubsystemMessage.RestartSubsystem message) {
		if (_restarting) {
			var info = "PROJECTIONS SUBSYSTEM: Not restarting because the subsystem is already being restarted.";
			Logger.Information(info);
			message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.InvalidSubsystemRestart("Restarting", info));
			return;
		}

		if (_subsystemState != SubsystemState.Started) {
			var info =
				$"PROJECTIONS SUBSYSTEM: Not restarting because the subsystem is not started. Current subsystem state: {_subsystemState}";
			Logger.Information(info);
			message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.InvalidSubsystemRestart(_subsystemState.ToString(), info));
			return;
		}

		Logger.Information("PROJECTIONS SUBSYSTEM: Restarting subsystem.");
		_restarting = true;
		StopComponents();
		message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.SubsystemRestarting());
	}

	public void Handle(ProjectionSubsystemMessage.ComponentStarted message) {
		if (message.InstanceCorrelationId != _instanceCorrelationId) {
			Logger.Debug(
				"PROJECTIONS SUBSYSTEM: Received component started for incorrect instance id. " +
				"Requested: {requestedCorrelationId} | Current: {instanceCorrelationId}",
				message.InstanceCorrelationId, _instanceCorrelationId);
			return;
		}

		if (_pendingComponentStarts <= 0 || _subsystemState != SubsystemState.Starting)
			return;

		Logger.Debug("PROJECTIONS SUBSYSTEM: Component '{componentName}' started for Instance: {instanceCorrelationId}",
			message.ComponentName, message.InstanceCorrelationId);
		_pendingComponentStarts--;
		_runningComponentCount++;

		if (_pendingComponentStarts == 0) {
			AllComponentsStarted();
		}
	}

	public void Handle(ProjectionSubsystemMessage.IODispatcherDrained message) {
		_runningDispatchers--;
		Logger.Information(
			"PROJECTIONS SUBSYSTEM: IO Dispatcher from {componentName} has been drained. {runningCount} of {totalCount} queues empty.",
			message.ComponentName, _runningDispatchers, _dispatcherCount);
		FinishStopping();
	}

	private void AllComponentsStarted() {
		Logger.Information("PROJECTIONS SUBSYSTEM: All components started for Instance: {instanceCorrelationId}",
			_instanceCorrelationId);
		_subsystemState = SubsystemState.Started;
		_runningDispatchers = _dispatcherCount;

		PublishInitialized();

		if (_nodeState != VNodeState.Leader) {
			Logger.Information("PROJECTIONS SUBSYSTEM: Node state is no longer Leader. Stopping projections. Current node state: {nodeState}",
				_nodeState);
			StopComponents();
		}
	}

	public void Handle(ProjectionSubsystemMessage.ComponentStopped message) {
		if (message.InstanceCorrelationId != _instanceCorrelationId) {
			Logger.Debug(
				"PROJECTIONS SUBSYSTEM: Received component stopped for incorrect correlation id. " +
				"Requested: {requestedCorrelationId} | Instance: {instanceCorrelationId}",
				message.InstanceCorrelationId, _instanceCorrelationId);
			return;
		}

		if (_subsystemState != SubsystemState.Stopping)
			return;

		Logger.Debug("PROJECTIONS SUBSYSTEM: Component '{componentName}' stopped for Instance: {instanceCorrelationId}",
			message.ComponentName, message.InstanceCorrelationId);
		_runningComponentCount--;
		if (_runningComponentCount < 0) {
			Logger.Warning("PROJECTIONS SUBSYSTEM: Got more component stopped messages than running components.");
			_runningComponentCount = 0;
		}

		FinishStopping();
	}

	private void FinishStopping() {
		if (_runningDispatchers > 0)
			return;
		if (_runningComponentCount > 0)
			return;

		Logger.Information(
			"PROJECTIONS SUBSYSTEM: All components stopped and dispatchers drained for Instance: {correlationId}",
			_instanceCorrelationId);
		_subsystemState = SubsystemState.Stopped;

		if (_restarting) {
			StartComponents();
			return;
		}

		if (_nodeState == VNodeState.Leader) {
			Logger.Information("PROJECTIONS SUBSYSTEM: Node state has changed to Leader. Starting projections.");
			StartComponents();
		}
	}

	private void PublishInitialized() {
		_subsystemInitialized.TrySetResult();
	}

	public Task Start() {
		if (_subsystemStarted == false) {
			_leaderInputQueue?.Start();
			_leaderOutputQueue?.Start();

			foreach (var queue in _coreWorkers)
				queue.Value.Start();
		}

		_subsystemStarted = true;

		return _subsystemInitialized.Task;
	}

	public async Task Stop() {
		if (_subsystemStarted) {
			await (_leaderInputQueue?.Stop() ?? Task.CompletedTask);
			foreach (var queue in _coreWorkers)
				await queue.Value.Stop();
		}

		_subsystemStarted = false;
	}

	public void Handle(CoreProjectionStatusMessage.Stopped message) {
		if (_startStandardProjections) {
			if (_standardProjections.Contains(message.Name)) {
				_standardProjections.Remove(message.Name);
				var envelope = new NoopEnvelope();
				LeaderInputQueue.Publish(new ProjectionManagementMessage.Command.Enable(envelope, message.Name,
					ProjectionManagementMessage.RunAs.System));
			}
		}
	}

	public void Handle(CoreProjectionStatusMessage.Started message) {
		_standardProjections.Remove(message.Name);
	}

	private enum SubsystemState {
		NotReady,
		Ready,
		Starting,
		Started,
		Stopping,
		Stopped
	}
}
