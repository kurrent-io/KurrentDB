// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Serilog;
using CoreResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ProjectionResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2 : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config;
	private readonly IReadStrategy _readStrategy;
	private readonly IPublisher _bus;
	private readonly ClaimsPrincipal _user;
	private CancellationTokenSource _cts;
	private Task _runTask;
	private long _totalEventsProcessed;
	private readonly ConcurrentDictionary<string, string> _partitionStates = new();

	public ProjectionEngineV2(
		ProjectionEngineV2Config config,
		IReadStrategy readStrategy,
		IPublisher bus,
		ClaimsPrincipal user) {
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
		_bus = bus ?? throw new ArgumentNullException(nameof(bus));
		_user = user ?? throw new ArgumentNullException(nameof(user));
	}

	public Task Start(TFPos checkpoint, CancellationToken ct) {
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_runTask = Task.Run(() => Run(checkpoint, _cts.Token), _cts.Token);
		return Task.CompletedTask;
	}

	public bool IsFaulted => _runTask?.IsFaulted ?? false;
	public Exception FaultException => _runTask?.Exception?.InnerException;
	public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

	public string GetPartitionState(string partitionKey) =>
		_partitionStates.TryGetValue(partitionKey, out var state) ? state : null;

	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint}", _config.ProjectionName, checkpoint);

		var partitionCount = _config.PartitionCount;

		// BiState projections use shared state (s[1]) that every event can modify.
		// This creates a total ordering dependency — true parallelism isn't possible.
		if (_config.SourceDefinition.IsBiState && partitionCount > 1) {
			Log.Warning("BiState projection {Name} forced to PartitionCount=1 (shared state requires sequential processing)",
				_config.ProjectionName);
			partitionCount = 1;
		}

		// Partition key is computed on the read loop thread using a dedicated handler instance.
		// This is single-threaded, so Jint thread safety is not a concern.
		using var partitionKeyHandler = _config.SourceDefinition.ByCustomPartitions
			? _config.StateHandlerFactory()
			: null;

		var getPartitionKey = BuildPartitionKeyFunction(partitionKeyHandler);

		var dispatcher = new PartitionDispatcher(partitionCount, getPartitionKey);

		var coordinator = new CheckpointCoordinator(
			partitionCount,
			_config.ProjectionName,
			_bus,
			_user);

		// Each partition gets its own state handler instance (Jint is not thread-safe).
		var partitionHandlers = new IProjectionStateHandler[partitionCount];
		var partitionTasks = new Task[partitionCount];
		for (int i = 0; i < partitionCount; i++) {
			var partitionIndex = i;
			partitionHandlers[i] = _config.StateHandlerFactory();
			var processor = new PartitionProcessor(
				partitionIndex,
				dispatcher.GetPartitionReader(partitionIndex),
				partitionHandlers[i],
				_config.ProjectionName,
				_config.SourceDefinition.IsBiState,
				_config.EmitEnabled,
				(sequence, buffer) => coordinator.ReportPartitionCheckpoint(partitionIndex, sequence, buffer),
				_partitionStates);
			partitionTasks[i] = Task.Run(() => processor.Run(ct), ct);
		}

		try {
			await RunReadLoop(checkpoint, dispatcher, ct);
			dispatcher.Complete();
		} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			dispatcher.Complete();
			throw;
		} catch (Exception ex) {
			Log.Error(ex, "ProjectionEngineV2 {Name} read loop failed", _config.ProjectionName);
			dispatcher.Complete(ex);
			throw;
		} finally {
			// Wait for all partition processors to drain
			try {
				await Task.WhenAll(partitionTasks);
			} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
				// Expected on cancellation
			}

			// Dispose per-partition state handlers
			foreach (var handler in partitionHandlers) {
				handler?.Dispose();
			}
		}
	}

	private async Task RunReadLoop(TFPos checkpoint, PartitionDispatcher dispatcher, CancellationToken ct) {
		long eventsProcessed = 0;
		long bytesProcessed = 0;
		var lastCheckpointTime = Stopwatch.GetTimestamp();
		var lastLogPosition = checkpoint;

		try {
			await foreach (var response in _readStrategy.ReadFrom(checkpoint, ct)) {
				switch (response) {
					case ReadResponse.EventReceived eventReceived:
						var coreEvent = eventReceived.Event;

						// Skip system events (event types starting with '$') — they are internal
						// infrastructure events (e.g. metadata, links) that projections don't process.
						if (coreEvent.Event.EventType.StartsWith("$"))
							break;

						var projEvent = ConvertToProjectionEvent(coreEvent);
						var logPosition = coreEvent.OriginalPosition
							?? new TFPos(coreEvent.Event.LogPosition, coreEvent.Event.TransactionPosition);

						await dispatcher.DispatchEvent(projEvent, logPosition, ct);

						eventsProcessed++;
						Interlocked.Increment(ref _totalEventsProcessed);
						bytesProcessed += coreEvent.Event.Data.Length + coreEvent.Event.Metadata.Length;
						lastLogPosition = logPosition;

						// Check if checkpoint is due
						var elapsedMs = GetElapsedMs(lastCheckpointTime);
						if (elapsedMs >= _config.CheckpointAfterMs &&
							(eventsProcessed >= _config.CheckpointHandledThreshold ||
							 bytesProcessed >= _config.CheckpointUnhandledBytesThreshold)) {
							await dispatcher.InjectCheckpointMarker(lastLogPosition, ct);
							eventsProcessed = 0;
							bytesProcessed = 0;
							lastCheckpointTime = Stopwatch.GetTimestamp();
						}
						break;

					// Ignore subscription infrastructure messages
					case ReadResponse.SubscriptionConfirmed:
					case ReadResponse.CheckpointReceived:
					case ReadResponse.SubscriptionCaughtUp:
					case ReadResponse.SubscriptionFellBehind:
						break;
				}
			}
		} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			// Cancellation is expected during shutdown — fall through to final checkpoint
		}

		// Inject a final checkpoint marker if there are unprocessed events
		if (eventsProcessed > 0) {
			await dispatcher.InjectCheckpointMarker(lastLogPosition, CancellationToken.None);
		}
	}

	/// <summary>
	/// Builds the partition key function for the dispatcher.
	/// For ByCustomPartitions, uses a dedicated handler instance (called single-threaded
	/// on the read loop). For ByStreams, uses stream ID. Otherwise returns empty string.
	/// </summary>
	#nullable enable
	private Func<ProjectionResolvedEvent, string?> BuildPartitionKeyFunction(
		IProjectionStateHandler? partitionKeyHandler) {
		if (_config.SourceDefinition.ByCustomPartitions) {
			return projEvent => {
				var checkpointTag = CheckpointTag.FromPosition(0,
					projEvent.Position.CommitPosition,
					projEvent.Position.PreparePosition);
				return partitionKeyHandler!.GetStatePartition(checkpointTag, null, projEvent);
			};
		}

		if (_config.SourceDefinition.ByStreams) {
			return projEvent => projEvent.EventStreamId;
		}

		return _ => "";
	}
	#nullable restore

	/// <summary>
	/// Converts a Core ResolvedEvent (struct from storage engine) to a Projections
	/// ResolvedEvent (class used by projection state handlers and partition dispatcher).
	/// This is necessary because the V2 namespace shadows Core.Data.ResolvedEvent
	/// with Processing.ResolvedEvent.
	/// </summary>
	internal static ProjectionResolvedEvent ConvertToProjectionEvent(CoreResolvedEvent coreEvent) {
		var e = coreEvent.Event;
		var link = coreEvent.Link;
		return new ProjectionResolvedEvent(
			positionStreamId: link?.EventStreamId ?? e.EventStreamId,
			positionSequenceNumber: link?.EventNumber ?? e.EventNumber,
			eventStreamId: e.EventStreamId,
			eventSequenceNumber: e.EventNumber,
			resolvedLinkTo: link is not null,
			position: coreEvent.OriginalPosition ?? new TFPos(e.LogPosition, e.TransactionPosition),
			eventId: e.EventId,
			eventType: e.EventType,
			isJson: e.IsJson,
			data: e.Data.Length > 0 ? System.Text.Encoding.UTF8.GetString(e.Data.Span) : null,
			metadata: e.Metadata.Length > 0 ? System.Text.Encoding.UTF8.GetString(e.Metadata.Span) : null);
	}

	private static double GetElapsedMs(long startTimestamp) {
		return Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
	}

	public async ValueTask DisposeAsync() {
		if (_cts is not null) {
			await _cts.CancelAsync();
			if (_runTask is not null) {
				try { await _runTask; } catch (OperationCanceledException) { }
			}
			_cts.Dispose();
		}
		await _readStrategy.DisposeAsync();
	}
}
