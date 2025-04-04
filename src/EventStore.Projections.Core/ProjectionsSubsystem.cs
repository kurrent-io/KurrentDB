using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Subsystems;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core {
	public record ProjectionSubsystemOptions(
		int ProjectionWorkerThreadCount,
		ProjectionType RunProjections,
		bool StartStandardProjections,
		TimeSpan ProjectionQueryExpiry,
		bool FaultOutOfOrderProjections,
		int CompilationTimeout,
		int ExecutionTimeout);

	public sealed class ProjectionsSubsystem : ISubsystem, ISubsystemFactory,
		IHandle<SystemMessage.SystemCoreReady>,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<CoreProjectionStatusMessage.Stopped>,
		IHandle<CoreProjectionStatusMessage.Started>,
		IHandle<ProjectionSubsystemMessage.RestartSubsystem>,
		IHandle<ProjectionSubsystemMessage.ComponentStarted>,
		IHandle<ProjectionSubsystemMessage.ComponentStopped>,
		IHandle<ProjectionSubsystemMessage.IODispatcherDrained> {

		public InMemoryBus LeaderMainBus {
			get { return _leaderMainBus; }
		}
		public InMemoryBus LeaderOutputBus {
			get { return _leaderOutputBus; }
		}

		public IPublisher LeaderQueue { get { return _leaderInputQueue; } }

		private readonly int _projectionWorkerThreadCount;
		private readonly ProjectionType _runProjections;
		private readonly bool _startStandardProjections;
		private readonly TimeSpan _projectionsQueryExpiry;
		private readonly ILogger _logger = Serilog.Log.ForContext<ProjectionsSubsystem>();
		public const int VERSION = 4;
		public const int CONTENT_TYPE_VALIDATION_VERSION = 4;

		private IQueuedHandler _leaderInputQueue;
		private readonly InMemoryBus _leaderMainBus;
		private InMemoryBus _leaderOutputBus;
		private IDictionary<Guid, IQueuedHandler> _coreQueues;
		private Dictionary<Guid, IPublisher> _queueMap;
		private bool _subsystemStarted;
		private int _subsystemInitialized;

		private readonly bool _faultOutOfOrderProjections;

		private readonly int _compilationTimeout;
		private readonly int _executionTimeout;

		private readonly int _componentCount;
		private readonly int _dispatcherCount;
		private bool _restarting;
		private int _pendingComponentStarts;
		private int _runningComponentCount;
		private int _runningDispatchers;

		private VNodeState _nodeState;
		private SubsystemState _subsystemState = SubsystemState.NotReady;
		private Guid _instanceCorrelationId;

		public IApplicationBuilder Configure(IApplicationBuilder builder) => builder
			.UseEndpoints(endpoints => endpoints.MapGrpcService<ProjectionManagement>());

		public IServiceCollection ConfigureServices(IServiceCollection services) => services
			.AddSingleton(provider =>
				new ProjectionManagement(_leaderInputQueue, provider.GetRequiredService<IAuthorizationProvider>()));

		public ProjectionsSubsystem(
			ProjectionSubsystemOptions projectionSubsystemOptions) {

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

			_leaderMainBus = new InMemoryBus("manager input bus");
			_subsystemInitialized = 0;
			_executionTimeout = projectionSubsystemOptions.ExecutionTimeout;
			_compilationTimeout = projectionSubsystemOptions.CompilationTimeout;
		}

		public ISubsystem Create(StandardComponents standardComponents) {
			_leaderInputQueue = QueuedHandler.CreateQueuedHandler(_leaderMainBus, "Projections Leader",
				standardComponents.QueueStatsManager, standardComponents.QueueTrackers);
			_leaderOutputBus = new InMemoryBus("ProjectionManagerAndCoreCoordinatorOutput");

			_leaderMainBus.Subscribe<ProjectionSubsystemMessage.RestartSubsystem>(this);
			_leaderMainBus.Subscribe<ProjectionSubsystemMessage.ComponentStarted>(this);
			_leaderMainBus.Subscribe<ProjectionSubsystemMessage.ComponentStopped>(this);
			_leaderMainBus.Subscribe<ProjectionSubsystemMessage.IODispatcherDrained>(this);
			_leaderMainBus.Subscribe<SystemMessage.SystemCoreReady>(this);
			_leaderMainBus.Subscribe<SystemMessage.StateChangeMessage>(this);

			var projectionsStandardComponents = new ProjectionsStandardComponents(
				_projectionWorkerThreadCount,
				_runProjections,
				_leaderOutputBus,
				_leaderInputQueue,
				_leaderMainBus,
				_faultOutOfOrderProjections,
				_compilationTimeout,
				_executionTimeout);

			CreateAwakerService(standardComponents);
			_coreQueues =
				ProjectionCoreWorkersNode.CreateCoreWorkers(standardComponents, projectionsStandardComponents);
			_queueMap = _coreQueues.ToDictionary(v => v.Key, v => (IPublisher)v.Value);

			ProjectionManagerNode.CreateManagerService(standardComponents, projectionsStandardComponents, _queueMap,
				_projectionsQueryExpiry);
			projectionsStandardComponents.LeaderMainBus.Subscribe<CoreProjectionStatusMessage.Stopped>(this);
			projectionsStandardComponents.LeaderMainBus.Subscribe<CoreProjectionStatusMessage.Started>(this);
			return this;
		}

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
				_logger.Debug("PROJECTIONS SUBSYSTEM: Not starting because node is not leader. Current node state: {nodeState}",
					_nodeState);
				return;
			}
			if (_subsystemState != SubsystemState.Ready && _subsystemState != SubsystemState.Stopped) {
				_logger.Debug("PROJECTIONS SUBSYSTEM: Not starting because system is not ready or stopped. Current Subsystem state: {subsystemState}",
					_subsystemState);
				return;
			}
			if (_runningComponentCount > 0) {
				_logger.Warning("PROJECTIONS SUBSYSTEM: Subsystem is stopped, but components are still running.");
				return;
			}

			_subsystemState = SubsystemState.Starting;
			_restarting = false;
			_instanceCorrelationId = Guid.NewGuid();
			_logger.Information("PROJECTIONS SUBSYSTEM: Starting components for Instance: {instanceCorrelationId}", _instanceCorrelationId);
			_pendingComponentStarts = _componentCount;
			_leaderMainBus.Publish(new ProjectionSubsystemMessage.StartComponents(_instanceCorrelationId));
		}

		private void StopComponents() {
			if (_subsystemState != SubsystemState.Started) {
				_logger.Debug("PROJECTIONS SUBSYSTEM: Not stopping because subsystem is not in a started state. Current Subsystem state: {state}", _subsystemState);
				return;
			}

			_logger.Information("PROJECTIONS SUBSYSTEM: Stopping components for Instance: {instanceCorrelationId}", _instanceCorrelationId);
			_subsystemState = SubsystemState.Stopping;
			_leaderMainBus.Publish(new ProjectionSubsystemMessage.StopComponents(_instanceCorrelationId));
		}

		public void Handle(ProjectionSubsystemMessage.RestartSubsystem message) {
			if (_restarting) {
				var info = "PROJECTIONS SUBSYSTEM: Not restarting because the subsystem is already being restarted.";
				_logger.Information(info);
				message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.InvalidSubsystemRestart("Restarting", info));
				return;
			}

			if (_subsystemState != SubsystemState.Started) {
				var info =
					$"PROJECTIONS SUBSYSTEM: Not restarting because the subsystem is not started. Current subsystem state: {_subsystemState}";
				_logger.Information(info);
				message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.InvalidSubsystemRestart(_subsystemState.ToString(), info));
				return;
			}

			_logger.Information("PROJECTIONS SUBSYSTEM: Restarting subsystem.");
			_restarting = true;
			StopComponents();
			message.ReplyEnvelope.ReplyWith(new ProjectionSubsystemMessage.SubsystemRestarting());
		}

		public void Handle(ProjectionSubsystemMessage.ComponentStarted message) {
			if (message.InstanceCorrelationId != _instanceCorrelationId) {
				_logger.Debug(
					"PROJECTIONS SUBSYSTEM: Received component started for incorrect instance id. " +
					"Requested: {requestedCorrelationId} | Current: {instanceCorrelationId}",
					message.InstanceCorrelationId, _instanceCorrelationId);
				return;
			}

			if (_pendingComponentStarts <= 0 || _subsystemState != SubsystemState.Starting)
				return;

			_logger.Debug("PROJECTIONS SUBSYSTEM: Component '{componentName}' started for Instance: {instanceCorrelationId}",
				message.ComponentName, message.InstanceCorrelationId);
			_pendingComponentStarts--;
			_runningComponentCount++;

			if (_pendingComponentStarts == 0) {
				AllComponentsStarted();
			}
		}

		public void Handle(ProjectionSubsystemMessage.IODispatcherDrained message) {
			_runningDispatchers--;
			_logger.Information(
				"PROJECTIONS SUBSYSTEM: IO Dispatcher from {componentName} has been drained. {runningCount} of {totalCount} queues empty.",
				message.ComponentName, _runningDispatchers, _dispatcherCount);
			FinishStopping();
		}

		private void AllComponentsStarted() {
			_logger.Information("PROJECTIONS SUBSYSTEM: All components started for Instance: {instanceCorrelationId}",
				_instanceCorrelationId);
			_subsystemState = SubsystemState.Started;
			_runningDispatchers = _dispatcherCount;

			PublishInitialized();

			if (_nodeState != VNodeState.Leader) {
				_logger.Information("PROJECTIONS SUBSYSTEM: Node state is no longer Leader. Stopping projections. Current node state: {nodeState}",
					_nodeState);
				StopComponents();
			}
		}

		public void Handle(ProjectionSubsystemMessage.ComponentStopped message) {
			if (message.InstanceCorrelationId != _instanceCorrelationId) {
				_logger.Debug(
					"PROJECTIONS SUBSYSTEM: Received component stopped for incorrect correlation id. " +
					"Requested: {requestedCorrelationId} | Instance: {instanceCorrelationId}",
					message.InstanceCorrelationId, _instanceCorrelationId);
				return;
			}

			if (_subsystemState != SubsystemState.Stopping)
				return;

			_logger.Debug("PROJECTIONS SUBSYSTEM: Component '{componentName}' stopped for Instance: {instanceCorrelationId}",
				message.ComponentName, message.InstanceCorrelationId);
			_runningComponentCount--;
			if (_runningComponentCount < 0) {
				_logger.Warning("PROJECTIONS SUBSYSTEM: Got more component stopped messages than running components.");
				_runningComponentCount = 0;
			}

			FinishStopping();
		}

		private void FinishStopping() {
			if (_runningDispatchers > 0)
				return;
			if (_runningComponentCount > 0)
				return;

			_logger.Information(
				"PROJECTIONS SUBSYSTEM: All components stopped and dispatchers drained for Instance: {correlationId}",
				_instanceCorrelationId);
			_subsystemState = SubsystemState.Stopped;

			if (_restarting) {
				StartComponents();
				return;
			}

			if (_nodeState == VNodeState.Leader) {
				_logger.Information("PROJECTIONS SUBSYSTEM: Node state has changed to Leader. Starting projections.");
				StartComponents();
			}
		}

		private void PublishInitialized() {
			if (Interlocked.CompareExchange(ref _subsystemInitialized, 1, 0) == 0) {
				_leaderOutputBus.Publish(new SystemMessage.SubSystemInitialized("Projections"));
			}
		}

		public IReadOnlyList<Task> Start() {
			var tasks = new List<Task>();
			if (_subsystemStarted == false) {
				if (_leaderInputQueue != null)
					tasks.Add(_leaderInputQueue.Start());
				foreach (var queue in _coreQueues)
					tasks.Add(queue.Value.Start());
			}

			_subsystemStarted = true;
			return tasks;
		}

		public void Stop() {
			if (_subsystemStarted) {
				if (_leaderInputQueue != null)
					_leaderInputQueue.Stop();
				foreach (var queue in _coreQueues)
					queue.Value.Stop();
			}

			_subsystemStarted = false;
		}

		private readonly List<string> _standardProjections = new List<string> {
			"$by_category",
			"$stream_by_category",
			"$streams",
			"$by_event_type",
			"$by_correlation_id"
		};

		public void Handle(CoreProjectionStatusMessage.Stopped message) {
			if (_startStandardProjections) {
				if (_standardProjections.Contains(message.Name)) {
					_standardProjections.Remove(message.Name);
					var envelope = new NoopEnvelope();
					_leaderMainBus.Publish(new ProjectionManagementMessage.Command.Enable(envelope, message.Name,
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
}
