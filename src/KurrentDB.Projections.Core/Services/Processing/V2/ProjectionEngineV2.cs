// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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

	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint}", _config.ProjectionName, checkpoint);

		var dispatcher = new PartitionDispatcher(
			_config.PartitionCount,
			GetPartitionKeyFunction());

		var coordinator = new CheckpointCoordinator(
			_config.PartitionCount,
			_config.ProjectionName,
			_bus,
			_user);

		// Start partition processor tasks
		var partitionTasks = new Task[_config.PartitionCount];
		for (int i = 0; i < _config.PartitionCount; i++) {
			var partitionIndex = i;
			var processor = new PartitionProcessor(
				partitionIndex,
				dispatcher.GetPartitionReader(partitionIndex),
				_config.StateHandler,
				_config.ProjectionName,
				(sequence, buffer) => coordinator.ReportPartitionCheckpoint(partitionIndex, sequence, buffer));
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
		}
	}

	private async Task RunReadLoop(TFPos checkpoint, PartitionDispatcher dispatcher, CancellationToken ct) {
		long eventsProcessed = 0;
		long bytesProcessed = 0;
		var lastCheckpointTime = Stopwatch.GetTimestamp();
		var lastLogPosition = checkpoint;

		await foreach (var response in _readStrategy.ReadFrom(checkpoint, ct)) {
			switch (response) {
				case ReadResponse.EventReceived eventReceived:
					var coreEvent = eventReceived.Event;
					var projEvent = ConvertToProjectionEvent(coreEvent);
					var logPosition = coreEvent.OriginalPosition
						?? new TFPos(coreEvent.Event.LogPosition, coreEvent.Event.TransactionPosition);

					await dispatcher.DispatchEvent(projEvent, logPosition, ct);

					eventsProcessed++;
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

		// Inject a final checkpoint marker if there are unprocessed events
		if (eventsProcessed > 0) {
			await dispatcher.InjectCheckpointMarker(lastLogPosition, ct);
		}
	}

	/// <summary>
	/// Builds the partition key function for the dispatcher.
	/// The dispatcher works with Projections ResolvedEvent (due to V2 namespace shadowing).
	/// </summary>
	private Func<ProjectionResolvedEvent, string> GetPartitionKeyFunction() {
		if (_config.SourceDefinition.ByCustomPartitions) {
			return projEvent => {
				var checkpointTag = CheckpointTag.FromPosition(0,
					projEvent.Position.CommitPosition,
					projEvent.Position.PreparePosition);
				return _config.StateHandler.GetStatePartition(checkpointTag, null, projEvent);
			};
		}

		if (_config.SourceDefinition.ByStreams) {
			return projEvent => projEvent.EventStreamId;
		}

		return _ => "";
	}

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
