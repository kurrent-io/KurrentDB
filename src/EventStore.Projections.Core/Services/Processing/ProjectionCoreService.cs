// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing.Strategies;
using Serilog;

namespace EventStore.Projections.Core.Services.Processing;

public class ProjectionCoreService
	: IHandle<ProjectionCoreServiceMessage.StartCore>,
		IHandle<ProjectionCoreServiceMessage.StopCore>,
		IHandle<ProjectionCoreServiceMessage.CoreTick>,
		IHandle<CoreProjectionManagementMessage.CreateAndPrepare>,
		IHandle<CoreProjectionManagementMessage.CreatePrepared>,
		IHandle<CoreProjectionManagementMessage.Dispose>,
		IHandle<CoreProjectionManagementMessage.Start>,
		IHandle<CoreProjectionManagementMessage.LoadStopped>,
		IHandle<CoreProjectionManagementMessage.Stop>,
		IHandle<CoreProjectionManagementMessage.Kill>,
		IHandle<CoreProjectionManagementMessage.GetState>,
		IHandle<CoreProjectionManagementMessage.GetResult>,
		IHandle<CoreProjectionProcessingMessage.CheckpointCompleted>,
		IHandle<CoreProjectionProcessingMessage.CheckpointLoaded>,
		IHandle<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>,
		IHandle<CoreProjectionProcessingMessage.RestartRequested>,
		IHandle<CoreProjectionProcessingMessage.Failed>,
		IHandle<ProjectionCoreServiceMessage.StopCoreTimeout>,
		IHandle<CoreProjectionStatusMessage.Suspended> {
	public const string SubComponentName = "ProjectionCoreService";

	private readonly Guid _workerId;
	private readonly IPublisher _publisher;
	private readonly IPublisher _inputQueue;
	private readonly ILogger _logger = Log.ForContext<ProjectionCoreService>();

	private readonly Dictionary<Guid, CoreProjection> _projections = new Dictionary<Guid, CoreProjection>();

	private readonly IODispatcher _ioDispatcher;

	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;

	private readonly ITimeProvider _timeProvider;
	private readonly ProcessingStrategySelector _processingStrategySelector;

	private bool _stopping;
	private readonly Dictionary<Guid, CoreProjection> _suspendingProjections = new Dictionary<Guid, CoreProjection>();
	private Guid _stopQueueId = Guid.Empty;
	private int _projectionStopTimeoutMs = 5000;
	private readonly ProjectionStateHandlerFactory _factory;

	public ProjectionCoreService(
		Guid workerId,
		IPublisher inputQueue,
		IPublisher publisher,
		ReaderSubscriptionDispatcher subscriptionDispatcher,
		ITimeProvider timeProvider,
		IODispatcher ioDispatcher,
		ProjectionsStandardComponents configuration) {
		_workerId = workerId;
		_inputQueue = inputQueue;
		_publisher = publisher;
		_ioDispatcher = ioDispatcher;
		_subscriptionDispatcher = subscriptionDispatcher;
		_timeProvider = timeProvider;
		_processingStrategySelector = new ProcessingStrategySelector(_subscriptionDispatcher, configuration.MaxProjectionStateSize);
		_factory = new ProjectionStateHandlerFactory(TimeSpan.FromMilliseconds(configuration.ProjectionCompilationTimeout),
			TimeSpan.FromMilliseconds(configuration.ProjectionExecutionTimeout));
	}

	public ILogger Logger {
		get { return _logger; }
	}

	public void Handle(ProjectionCoreServiceMessage.StartCore message) {
		_publisher.Publish(new ProjectionCoreServiceMessage.SubComponentStarted(
			SubComponentName, message.InstanceCorrelationId));
	}

	public void Handle(ProjectionCoreServiceMessage.StopCore message) {
		_stopQueueId = message.QueueId;
		StopProjections();
	}

	private void StopProjections() {
		_stopping = true;

		_ioDispatcher.StartDraining(
			() => _publisher.Publish(new ProjectionSubsystemMessage.IODispatcherDrained(SubComponentName)));

		var allProjections = _projections.Values.ToArray();
		foreach (var projection in allProjections)
		{
			var requiresStopping = projection.Suspend();
			if (requiresStopping) {
				_suspendingProjections.Add(projection._projectionCorrelationId, projection);
			}
		}

		if (_suspendingProjections.IsEmpty()) {
			FinishStopping();
		} else {
			_publisher.Publish(TimerMessage.Schedule.Create(
				TimeSpan.FromMilliseconds(_projectionStopTimeoutMs),
				_inputQueue,
				new ProjectionCoreServiceMessage.StopCoreTimeout(_stopQueueId)));
		}
	}

	public void Handle(ProjectionCoreServiceMessage.StopCoreTimeout message) {
		if (message.QueueId != _stopQueueId) return;
		_logger.Debug("PROJECTIONS: Suspending projections in Projection Core Service timed out. Force stopping.");
		FinishStopping();
	}

	public void Handle(CoreProjectionStatusMessage.Suspended message) {
		if (!_stopping) return;

		_suspendingProjections.Remove(message.ProjectionId);
		if (_suspendingProjections.Count == 0) {
			FinishStopping();
		}
	}

	private void FinishStopping() {
		if (!_stopping) return;

		_projections.Clear();
		_stopping = false;
		_publisher.Publish(new ProjectionCoreServiceMessage.SubComponentStopped(
			nameof(ProjectionCoreService), _stopQueueId));
		_stopQueueId = Guid.Empty;
	}

	public void Handle(ProjectionCoreServiceMessage.CoreTick message) {
		message.Action();
	}

	public void Handle(CoreProjectionManagementMessage.CreateAndPrepare message) {
		try {
			//TODO: factory method can throw
			var stateHandler = CreateStateHandler(_factory,
				_logger,
				message.HandlerType,
				message.Query,
				message.EnableContentTypeValidation,
				message.Config.ProjectionExecutionTimeout);

			string name = message.Name;
			var sourceDefinition = ProjectionSourceDefinition.From(stateHandler.GetSourceDefinition());

			var projectionVersion = message.Version;
			var projectionConfig = message.Config;
			var namesBuilder = new ProjectionNamesBuilder(name, sourceDefinition);

			var projectionProcessingStrategy = _processingStrategySelector.CreateProjectionProcessingStrategy(
				name,
				projectionVersion,
				namesBuilder,
				sourceDefinition,
				projectionConfig,
				stateHandler,
				message.HandlerType,
				message.Query,
				message.EnableContentTypeValidation);

			CreateCoreProjection(message.ProjectionId, projectionConfig.RunAs, projectionProcessingStrategy);
			_publisher.Publish(
				new CoreProjectionStatusMessage.Prepared(
					message.ProjectionId, sourceDefinition));
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.Faulted(message.ProjectionId, ex.Message));
		}
	}

	public void Handle(CoreProjectionManagementMessage.CreatePrepared message) {
		try {
			var name = message.Name;
			var sourceDefinition = ProjectionSourceDefinition.From(message.SourceDefinition);
			var projectionVersion = message.Version;
			var projectionConfig = message.Config;
			var namesBuilder = new ProjectionNamesBuilder(name, sourceDefinition);

			var projectionProcessingStrategy = _processingStrategySelector.CreateProjectionProcessingStrategy(
				name,
				projectionVersion,
				namesBuilder,
				sourceDefinition,
				projectionConfig,
				null,
				message.HandlerType,
				message.Query,
				message.EnableContentTypeValidation);

			CreateCoreProjection(message.ProjectionId, projectionConfig.RunAs, projectionProcessingStrategy);
			_publisher.Publish(
				new CoreProjectionStatusMessage.Prepared(
					message.ProjectionId, sourceDefinition));
		} catch (Exception ex) {
			_publisher.Publish(
				new CoreProjectionStatusMessage.Faulted(message.ProjectionId, ex.Message));
		}
	}

	private void CreateCoreProjection(
		Guid projectionCorrelationId, ClaimsPrincipal runAs, ProjectionProcessingStrategy processingStrategy) {
		var projection = processingStrategy.Create(
			projectionCorrelationId,
			_inputQueue,
			_workerId,
			runAs,
			_publisher,
			_ioDispatcher,
			_subscriptionDispatcher,
			_timeProvider);
		_projections.Add(projectionCorrelationId, projection);
	}

	public void Handle(CoreProjectionManagementMessage.Dispose message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection)) {
			_projections.Remove(message.ProjectionId);
			projection.Dispose();
		}
	}

	public void Handle(CoreProjectionManagementMessage.Start message) {
		var projection = _projections[message.ProjectionId];
		projection.Start();
	}

	public void Handle(CoreProjectionManagementMessage.LoadStopped message) {
		var projection = _projections[message.ProjectionId];
		projection.LoadStopped();
	}

	public void Handle(CoreProjectionManagementMessage.Stop message) {
		var projection = _projections[message.ProjectionId];
		projection.Stop();
	}

	public void Handle(CoreProjectionManagementMessage.Kill message) {
		var projection = _projections[message.ProjectionId];
		projection.Kill();
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionManagementMessage.GetResult message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionProcessingMessage.CheckpointCompleted message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionProcessingMessage.RestartRequested message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public void Handle(CoreProjectionProcessingMessage.Failed message) {
		CoreProjection projection;
		if (_projections.TryGetValue(message.ProjectionId, out projection))
			projection.Handle(message);
	}

	public static IProjectionStateHandler CreateStateHandler(ProjectionStateHandlerFactory factory,
		ILogger logger,
		string handlerType,
		string query,
		bool enableContentTypeValidation,
		int? projectionExecutionTimeout) {
		var stateHandler = factory.Create(
			handlerType,
			query,
			enableContentTypeValidation,
			projectionExecutionTimeout,
			logger: logger.Verbose);
		return stateHandler;
	}
}
