// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Standard;
using Serilog;
using CoreResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ProjectionResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public sealed class ProjectionEngineV2(
	ProjectionEngineV2Config config,
	IReadStrategy readStrategy,
	ISystemClient client,
	ClaimsPrincipal user) : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config = config ?? throw new ArgumentNullException(nameof(config));
	private readonly IReadStrategy _readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
	private readonly ISystemClient _client = client ?? throw new ArgumentNullException(nameof(client));
	private readonly ClaimsPrincipal _user = user ?? throw new ArgumentNullException(nameof(user));
	private readonly CancellationTokenSource _cts = new();
	private Task _runTask;
	private long _totalEventsProcessed;
	private readonly ConcurrentDictionary<string, string> _partitionStates = new();

	public void Start(TFPos checkpoint) {
		_runTask = Run(checkpoint, _cts.Token);
	}

	public async ValueTask DisposeAsync() {
		if (_cts.IsCancellationRequested)
			return;

		await _cts.CancelAsync();
		if (_runTask is { } runTask)
			await runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		_cts.Dispose();
	}

	public bool IsFaulted => _runTask?.IsFaulted ?? false;
	public bool IsStopped => _runTask?.IsCompleted ?? false;
	public bool IsStopping => _runTask is { IsCompleted: false } && _cts.IsCancellationRequested;
	public Exception FaultException => _runTask?.Exception?.InnerException;
	public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

	public string GetPartitionState(string partitionKey) =>
		_partitionStates.TryGetValue(partitionKey, out var state) ? state : null;

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint}", _config.ProjectionName, checkpoint);

		var partitionCount = _config.PartitionCount;

		// todo: consider limiting partitionCount to 1 if the projection itself does not partition.
		// BiState projections use shared state (s[1]) that every event can modify.
		// This creates a total ordering dependency — true parallelism isn't possible.
		if (_config.SourceDefinition.IsBiState && partitionCount > 1) {
			Log.Warning("BiState projection {Name} forced to PartitionCount=1 (shared state requires sequential processing)",
				_config.ProjectionName);
			partitionCount = 1;
		}

		// Partition key is computed on the read loop thread using a dedicated handler instance.
		// Dispatcher is single-threaded, so Jint thread safety is not a concern.
		using var partitionKeyHandler = _config.SourceDefinition.ByCustomPartitions
			? _config.StateHandlerFactory.Invoke()
			: null;

		var getPartitionKey = BuildPartitionKeyFunction(partitionKeyHandler);

		var dispatcher = new PartitionDispatcher(partitionCount, getPartitionKey);

		var coordinator = new CheckpointCoordinator(partitionCount, _config.ProjectionName, dispatcher, _client, _user);

		// Each partition gets its own state handler instance (Jint is not thread-safe).
		var partitionHandlers = new IProjectionStateHandler[partitionCount];
		var partitionTasks = new Task[partitionCount];
		// Let the partitions drain & checkpoint rather than cancelling them.
		// We stop them by completing their channels via dispatcher.Complete().
		var partitionCt = CancellationToken.None;
		for (int i = 0; i < partitionCount; i++) {
			partitionHandlers[i] = _config.StateHandlerFactory.Invoke();
			var processor = new PartitionProcessor(
				i,
				dispatcher.GetPartitionReader(i),
				partitionHandlers[i],
				_config.ProjectionName,
				_config.SourceDefinition.IsBiState,
				_config.EmitEnabled,
				coordinator.ReportPartitionCheckpoint,
				loadPersistedState: partitionKey => LoadPersistedPartitionState(partitionKey, ct),
				sharedPartitionStates: _partitionStates);
			partitionTasks[i] = Task.Run(() => processor.Run(partitionCt), partitionCt);
		}

		try {
			await RunReadLoop(checkpoint, dispatcher, coordinator, ct);
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
			Log.Information("ProjectionEngineV2 {Name} stopped", _config.ProjectionName);
		}
	}

	private async Task RunReadLoop(TFPos checkpoint, PartitionDispatcher dispatcher, CheckpointCoordinator coordinator, CancellationToken ct) {
		long eventsProcessed = 0;
		long bytesProcessed = 0;
		var lastCheckpointTime = Instant.Now;
		var lastLogPosition = checkpoint;

		// When the projection declares a specific set of event types, drop anything else here.
		// Read-level filters cover the stream/category dimension only; without this, Jint's Handle
		// overwrites partition state with the event body when no handler matches (matches V1's
		// EventFilter.Passes behaviour).
		var handledEventTypes = _config.SourceDefinition.AllEvents
			? null
			: new HashSet<string>(_config.SourceDefinition.Events ?? [], StringComparer.Ordinal);

		try {
			await foreach (var response in _readStrategy.ReadFrom(checkpoint, ct)) {
				switch (response) {
					case ReadResponse.EventReceived eventReceived:
						var coreEvent = eventReceived.Event;
						var logPosition = coreEvent.OriginalPosition
							?? throw new InvalidOperationException("OriginalPosition was not present. Likely a stream read with explicit transaction");

						// System events (event types starting with '$') are normally skipped,
						// but tombstone markers need to be routed so projections can handle $deleted.
						if (coreEvent.Event.EventType.StartsWith('$')) {
							var projEvent = ConvertToProjectionEvent(coreEvent);
							// note: HandlesDeletedNotifications is only true when partitioning by stream
							if (_config.SourceDefinition.HandlesDeletedNotifications && StreamDeletedHelper.IsStreamDeletedEvent(
								    projEvent.EventStreamId, projEvent.EventType, projEvent.Data,
								    out var deletedPartitionStreamId)) {
								await dispatcher.DispatchPartitionDeleted(deletedPartitionStreamId, logPosition, ct);
							} else {
								break;
							}
						} else {
							var projEvent = ConvertToProjectionEvent(coreEvent);
							if (handledEventTypes is not null && !handledEventTypes.Contains(projEvent.EventType))
								break;
							await dispatcher.DispatchEvent(projEvent, logPosition, ct);
						}

						eventsProcessed++;
						Interlocked.Increment(ref _totalEventsProcessed);
						bytesProcessed += coreEvent.Event.Data.Length + coreEvent.Event.Metadata.Length;
						lastLogPosition = logPosition;

						// Check if checkpoint is due
						var elapsedMs = Instant.Now.ElapsedTimeSince(lastCheckpointTime).TotalMilliseconds;
						if (elapsedMs >= _config.CheckpointAfterMs &&
						    (eventsProcessed >= _config.CheckpointHandledThreshold ||
						     bytesProcessed >= _config.CheckpointUnhandledBytesThreshold)) {
							await coordinator.InjectCheckpointMarker(lastLogPosition, ct);
							eventsProcessed = 0;
							bytesProcessed = 0;
							lastCheckpointTime = Instant.Now;
						}

						break;

					// Ignore subscription infrastructure messages
					case ReadResponse.CheckpointReceived checkpointReceived:
						lastLogPosition = new TFPos(
							commitPosition: (long)checkpointReceived.CommitPosition,
							preparePosition: (long)checkpointReceived.PreparePosition);
						// todo: consider checkpointing here
						break;
					case ReadResponse.SubscriptionConfirmed:
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
			await coordinator.InjectCheckpointMarker(lastLogPosition, CancellationToken.None);
		}
	}

#nullable enable
	/// <summary>
	/// Builds the partition key function for the dispatcher.
	/// For ByCustomPartitions, uses a dedicated handler instance (called single-threaded
	/// on the read loop). For ByStreams, uses stream ID. Otherwise returns empty string.
	/// </summary>
	private Func<ProjectionResolvedEvent, string?> BuildPartitionKeyFunction(IProjectionStateHandler? partitionKeyHandler) {
		if (_config.SourceDefinition.ByCustomPartitions) {
			return projEvent => {
				var checkpointTag = CheckpointTag.FromPosition(0, projEvent.Position.CommitPosition, projEvent.Position.PreparePosition);
				return partitionKeyHandler!.GetStatePartition(checkpointTag, null, projEvent);
			};
		}

		if (_config.SourceDefinition.ByStreams) {
			return projEvent => projEvent.EventStreamId;
		}

		return _ => "";
	}

	/// <summary>
	/// Loads persisted partition state from the state stream after a restart.
	/// Returns the state JSON if found, null otherwise.
	/// </summary>
	private async ValueTask<string?> LoadPersistedPartitionState(string partitionKey, CancellationToken ct) {
		var stateStreamId = ProjectionNamesBuilder.MakeStateStreamName(_config.ProjectionName, partitionKey);
		try {
			var lastEvent = await _client.Reading.ReadStreamLastEvent(stateStreamId, ct);
			if (lastEvent is null)
				return null;

			return Encoding.UTF8.GetString(lastEvent.Value.Event.Data.Span);
		} catch (ReadResponseException.StreamNotFound) {
			return null;
		}
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
		return new(
			positionStreamId: link?.EventStreamId ?? e.EventStreamId,
			positionSequenceNumber: link?.EventNumber ?? e.EventNumber,
			eventStreamId: e.EventStreamId,
			eventSequenceNumber: e.EventNumber,
			resolvedLinkTo: link is not null,
			position: coreEvent.OriginalPosition
				?? throw new InvalidOperationException("OriginalPosition was not present. Likely a stream read with explicit transaction"),
			eventId: e.EventId,
			eventType: e.EventType,
			isJson: e.IsJson,
			data: e.Data.Length > 0 ? Encoding.UTF8.GetString(e.Data.Span) : null,
			metadata: e.Metadata.Length > 0 ? Encoding.UTF8.GetString(e.Metadata.Span) : null);
	}
}
