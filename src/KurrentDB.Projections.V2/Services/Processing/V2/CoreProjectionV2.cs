// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// Adapts the async/Task-based V2 engine to the management layer's message-driven contract.
/// Implements ICoreProjectionControl so ProjectionCoreService can manage V2 projections
/// identically to V1 CoreProjection instances.
/// </summary>
/// <remarks>
/// This class is not thread safe, it runs on the projection worker's input queue
/// </remarks>
public sealed class CoreProjectionV2 : ICoreProjectionControl {
	static readonly ILogger Log = Serilog.Log.ForContext<CoreProjectionV2>();

	readonly Guid _projectionCorrelationId;
	readonly string _projectionName;
	readonly IPublisher _publisher;
	readonly IPublisher _inputQueue;
	readonly IPublisher _mainQueue;
	readonly IODispatcher _ioDispatcher;
	readonly ClaimsPrincipal _runAs;
	readonly IQuerySources _sourceDefinition;
	readonly Func<IProjectionStateHandler> _stateHandlerFactory;
	readonly ProjectionConfig _projectionConfig;
	readonly string _checkpointStreamId;

	ProjectionEngineV2 _engine;

	int _statisticsSequentialNumber;
	bool _disposed;

	public CoreProjectionV2(
		Guid projectionCorrelationId,
		string projectionName,
		IPublisher publisher,
		IPublisher inputQueue,
		IODispatcher ioDispatcher,
		ClaimsPrincipal runAs,
		IQuerySources sourceDefinition,
		Func<IProjectionStateHandler> stateHandlerFactory,
		ProjectionConfig projectionConfig,
		IPublisher mainQueue) {

		_projectionCorrelationId = projectionCorrelationId;
		_projectionName = projectionName;
		_publisher = publisher;
		_inputQueue = inputQueue;
		_mainQueue = mainQueue;
		_ioDispatcher = ioDispatcher;
		_runAs = runAs;
		_sourceDefinition = sourceDefinition;
		_stateHandlerFactory = stateHandlerFactory;
		_projectionConfig = projectionConfig;
		_checkpointStreamId = $"$projections-{projectionName}-checkpoint";
	}

	public Guid ProjectionCorrelationId => _projectionCorrelationId;

	public void Start() {
		ReadCheckpointAndStart();
	}

	public void LoadStopped() {
		_publisher.Publish(new CoreProjectionStatusMessage.Stopped(_projectionCorrelationId, _projectionName, completed: false));
	}

	public void Stop() {
		_ = StopEngine(
			new CoreProjectionStatusMessage.Stopped(_projectionCorrelationId, _projectionName, completed: false));
	}

	public void Kill() {
		_ = StopEngine(
			new CoreProjectionStatusMessage.Stopped(_projectionCorrelationId, _projectionName, completed: false));
	}

	public bool Suspend() {
		_ = StopEngine(
			new CoreProjectionStatusMessage.Suspended(_projectionCorrelationId));
		return true;
	}

	public void Handle(CoreProjectionManagementMessage.GetState message) {
		var partition = message.Partition;

		// Try in-memory state first (available immediately, no need to wait for checkpoint writes)
		string state = null;
		if (_engine is { } engine) {
			state = engine.GetPartitionState(partition ?? "");
		}

		if (state is not null) {
			_publisher.Publish(new CoreProjectionStatusMessage.StateReport(
				message.CorrelationId, _projectionCorrelationId, partition, state, position: null));
			return;
		}

		// Fall back to reading from the state stream (needed after restart for partitions
		// whose state was checkpointed before the restart but not yet re-processed)
		var stateStreamId = ProjectionNamesBuilder.MakeStateStreamName(_projectionName, partition);

		_ioDispatcher.ReadBackward(
			stateStreamId,
			-1, // last event
			1,
			resolveLinks: false,
			_runAs,
			completed => {
				string persistedState = null;
				if (completed.Result == ReadStreamResult.Success && completed.Events.Count > 0) {
					persistedState = System.Text.Encoding.UTF8.GetString(completed.Events[0].Event.Data.Span);
				}
				_publisher.Publish(new CoreProjectionStatusMessage.StateReport(
					message.CorrelationId, _projectionCorrelationId, partition, persistedState, position: null));
			});
	}

	public void Handle(CoreProjectionManagementMessage.GetResult message) {
		var partition = message.Partition;

		// Try in-memory state first
		string result = null;
		if (_engine is { } engine) {
			result = engine.GetPartitionState(partition ?? "");
		}

		if (result is not null) {
			_publisher.Publish(new CoreProjectionStatusMessage.ResultReport(
				message.CorrelationId, _projectionCorrelationId, partition, result, position: null));
			return;
		}

		// Fall back to reading from the state stream
		var stateStreamId = ProjectionNamesBuilder.MakeStateStreamName(_projectionName, partition);

		_ioDispatcher.ReadBackward(
			stateStreamId,
			-1, // last event
			1,
			resolveLinks: false,
			_runAs,
			completed => {
				string persistedResult = null;
				if (completed.Result == ReadStreamResult.Success && completed.Events.Count > 0) {
					persistedResult = System.Text.Encoding.UTF8.GetString(completed.Events[0].Event.Data.Span);
				}
				_publisher.Publish(new CoreProjectionStatusMessage.ResultReport(
					message.CorrelationId, _projectionCorrelationId, partition, persistedResult, position: null));
			});
	}

	// V2 engine handles checkpoints internally — no-op for V1 processing messages
	public void Handle(CoreProjectionProcessingMessage.CheckpointCompleted message) { }
	public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) { }
	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) { }
	public void Handle(CoreProjectionProcessingMessage.RestartRequested message) { }
	public void Handle(CoreProjectionProcessingMessage.Failed message) {
		SetFaulted(message.Reason);
	}

	public void Dispose() {
		if (_disposed) return;
		_disposed = true;
		_ = StopEngine();
	}

	void ReadCheckpointAndStart() {
		_ioDispatcher.ReadBackward(
			_checkpointStreamId,
			-1,
			1,
			resolveLinks: false,
			_runAs,
			completed => {
				var checkpoint = TFPos.Invalid;
				if (completed.Result == ReadStreamResult.Success && completed.Events.Count > 0) {
					try {
						var data = completed.Events[0].Event.Data.Span;
						using var doc = JsonDocument.Parse(data.ToArray());
						var commitPosition = doc.RootElement.GetProperty("commitPosition").GetInt64();
						var preparePosition = doc.RootElement.GetProperty("preparePosition").GetInt64();
						checkpoint = new TFPos(commitPosition, preparePosition);
					} catch (Exception ex) {
						Log.Warning(ex, "Failed to parse checkpoint for {Projection}, starting from beginning", _projectionName);
					}
				}

				StartEngine(checkpoint == TFPos.Invalid ? new TFPos(0, 0) : checkpoint);
			});
	}

	void StartEngine(TFPos checkpoint) {
		try {
			var readStrategy = ReadStrategyFactory.Create(_sourceDefinition, _mainQueue, _runAs);

			var config = new ProjectionEngineV2Config {
				ProjectionName = _projectionName,
				SourceDefinition = _sourceDefinition,
				StateHandlerFactory = _stateHandlerFactory,
				MaxPartitionStateCacheSize = 100_000, // TEMP: replaced with wired value in a later task
				CheckpointAfterMs = _projectionConfig.CheckpointAfterMs,
				CheckpointHandledThreshold = _projectionConfig.CheckpointHandledThreshold,
				CheckpointUnhandledBytesThreshold = _projectionConfig.CheckpointUnhandledBytesThreshold,
				EmitEnabled = _projectionConfig.EmitEventEnabled
			};

			_engine = new ProjectionEngineV2(config, readStrategy, new SystemClient(_mainQueue), _runAs);
			_engine.Start(checkpoint);

			// Publish Started and begin stats reporting
			_publisher.Publish(new CoreProjectionStatusMessage.Started(_projectionCorrelationId, _projectionName));
			_ = StartStatisticsReporting(_engine);
		} catch (Exception ex) {
			Log.Error(ex, "V2CoreProjection {Name} failed to start engine", _projectionName);
			SetFaulted(ex.Message);
		}
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	async Task StartStatisticsReporting(ProjectionEngineV2 engine) {
		while (!engine.IsStopped) {
			await Task.Delay(1000);
			PublishStatistics(engine);
		}

		// Check if engine faulted
		if (engine.IsFaulted) {
			_publisher.Publish(new CoreProjectionProcessingMessage.Failed(
				_projectionCorrelationId,
				engine.FaultException?.Message ?? "V2 engine faulted"));
		}

		PublishStatistics(engine);
	}

	void PublishStatistics(ProjectionEngineV2 engine) {
		var stats = new ProjectionStatistics {
			Name = _projectionName,
			EffectiveName = _projectionName,
			ProjectionId = 0,
			Status = engine.IsFaulted
				? "Faulted"
				: engine.IsStopped
					? "Stopped"
					: engine.IsStopping
						? "Stopping"
						: "Running",
			StateReason = "",
			BufferedEvents = 0,
			EventsProcessedAfterRestart = (int)engine.TotalEventsProcessed
		};

		_publisher.Publish(new CoreProjectionStatusMessage.StatisticsReport(
			_projectionCorrelationId, stats, _statisticsSequentialNumber++));
	}

	async Task StopEngine(Message onSuccess = null) {
		if (_engine is { } engine) {
			_engine = null;
			await engine.DisposeAsync();
			// only notify management once on stop, and the stats loop will notify management of failures.
			if (onSuccess is not null && !engine.IsFaulted)
				_publisher.Publish(onSuccess);
		}
	}

	void SetFaulted(string reason) {
		_ = StopEngine();
		_publisher.Publish(new CoreProjectionStatusMessage.Faulted(_projectionCorrelationId, reason));
	}
}
