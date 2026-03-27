// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

// Chandy-Lamport style, collects a consistent snapshot across all partitions.
// At most one checkpoint can be in flight at a time because the partitions alternate between a pair of buffers.
public class CheckpointCoordinator(int partitionCount, string projectionName, PartitionDispatcher dispatcher, ISystemClient client, ClaimsPrincipal user) {
	private static readonly ILogger Log = Serilog.Log.ForContext<CheckpointCoordinator>();

	private readonly string _checkpointStreamId = $"$projections-{projectionName}-checkpoint";

	// When the _checkpointLock is taken there is a checkpoint in progress.
	private readonly AsyncExclusiveLock _checkpointLock = new();
	private readonly IReadOnlyOutputBuffer[] _collectedBuffers = new IReadOnlyOutputBuffer[partitionCount];
	private int _collectedCount;
	private Exception _checkpointError;

	// Starts a checkpoint. If one is in progress, waits for it to complete first.
	// Throws if a previous checkpoint failed.
	public async ValueTask InjectCheckpointMarker(TFPos logPosition, CancellationToken ct) {
		await _checkpointLock.AcquireAsync(ct);

		try {
			if (_checkpointError is { } error)
				throw error;

			Log.Debug("Injecting checkpoint marker at {LogPosition}", logPosition);
			_collectedCount = 0;
			Array.Clear(_collectedBuffers);
			await dispatcher.InjectCheckpointMarker(logPosition, ct);
		} catch {
			_checkpointLock.Release();
			throw;
		}
	}

	// Called concurrently by partition processors in response to checkpoint markers.
	// The last partition processor to reply triggers the writing of the checkpoint but is still not blocked by it.
	public void ReportPartitionCheckpoint(int partitionIndex, IReadOnlyOutputBuffer buffer) {
		_collectedBuffers[partitionIndex] = buffer;

		if (Interlocked.Increment(ref _collectedCount) == partitionCount) {
			_ = WriteCheckpoint();
		}
	}

	private async Task WriteCheckpoint() {
		try {
			var lastPosition = TFPos.Invalid;
			foreach (var buf in _collectedBuffers) {
				if (buf.LastLogPosition > lastPosition)
					lastPosition = buf.LastLogPosition;
			}

			Log.Information("Writing checkpoint for {Projection} at {Position}",
				projectionName, lastPosition);

			var writes = BuildStreamWrites(lastPosition);
			await client.Writing.WriteEvents(writes, requireLeader: true, user);

			Log.Debug("Checkpoint written for {Projection}", projectionName);
		} catch (Exception ex) {
			_checkpointError = new Exception($"Checkpoint write failed for {projectionName}", ex);
		} finally {
			_checkpointLock.Release();
		}
	}

	private LowAllocReadOnlyMemory<StreamWrite> BuildStreamWrites(TFPos checkpointPosition) {
		var writes = LowAllocReadOnlyMemory<StreamWrite>.Builder.Empty;

		// 1. Checkpoint event
		var checkpointData = Encoding.UTF8.GetBytes(
			$$"""{"commitPosition":{{checkpointPosition.CommitPosition}},"preparePosition":{{checkpointPosition.PreparePosition}}}""");
		writes = writes.Add(new StreamWrite(
			_checkpointStreamId,
			ExpectedVersion.Any,
			[new Event(Guid.NewGuid(), "$ProjectionCheckpoint", isJson: true, checkpointData, isPropertyMetadata: false, metadata: null)]));

		// 2. Emitted events
		foreach (var buffer in _collectedBuffers) {
			foreach (var emitted in buffer.EmittedEvents) {
				var e = emitted.Event;
				var data = e.Data != null ? Helper.UTF8NoBom.GetBytes(e.Data) : null;
				var metadata = SerializeExtraMetadata(e);
				writes = writes.Add(new StreamWrite(
					e.StreamId,
					ExpectedVersion.Any,
					[new Event(e.EventId, e.EventType, e.IsJson, data, isPropertyMetadata: false, metadata)]));
			}
		}

		// 3. State updates
		foreach (var buffer in _collectedBuffers) {
			foreach (var (_, (streamName, stateJson, expVer)) in buffer.DirtyStates) {
				var stateData = Encoding.UTF8.GetBytes(stateJson);
				writes = writes.Add(new StreamWrite(
					streamName,
					expVer,
					[new Event(Guid.NewGuid(), "Result", isJson: true, stateData, isPropertyMetadata: false, metadata: null)]));
			}
		}

		return writes.Build();
	}

	private static byte[] SerializeExtraMetadata(EmittedEvent e) {
		var extra = e.ExtraMetaData();
		if (extra == null)
			return null;

		var pairs = extra.ToList();
		if (pairs.Count == 0)
			return null;

		var sb = new StringBuilder();
		sb.Append('{');
		var first = true;
		foreach (var pair in pairs) {
			if (!first) sb.Append(',');
			first = false;
			// Key is a JSON property name, Value is already a JSON-encoded value
			sb.Append('"').Append(pair.Key).Append("\":").Append(pair.Value);
		}

		sb.Append('}');
		return Encoding.UTF8.GetBytes(sb.ToString());
	}
}
