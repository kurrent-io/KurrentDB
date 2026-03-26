// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

// Chandy-Lamport style, collects a consistent snapshot across all partitions.
public class CheckpointCoordinator(int partitionCount, string projectionName, ISystemClient client, ClaimsPrincipal user) {
	private static readonly ILogger Log = Serilog.Log.ForContext<CheckpointCoordinator>();

	private readonly string _checkpointStreamId = $"$projections-{projectionName}-checkpoint";

	private readonly IReadOnlyOutputBuffer[] _collectedBuffers = new IReadOnlyOutputBuffer[partitionCount];
	private ulong _currentMarkerSequence;
	private int _collectedCount;
	private readonly SemaphoreSlim _checkpointSemaphore = new(1, 1);
	private readonly Lock _lock = new();

	public async Task ReportPartitionCheckpoint(int partitionIndex, ulong markerSequence, IReadOnlyOutputBuffer buffer) {
		bool allCollected;
		lock (_lock) {
			if (_currentMarkerSequence != markerSequence) {
				_currentMarkerSequence = markerSequence;
				_collectedCount = 0;
				Array.Clear(_collectedBuffers);
			}

			_collectedBuffers[partitionIndex] = buffer;
			_collectedCount++;
			allCollected = _collectedCount == partitionCount;
		}

		if (allCollected) {
			await WriteCheckpoint(markerSequence);
		}
	}

	private async Task WriteCheckpoint(ulong markerSequence) {
		await _checkpointSemaphore.WaitAsync();
		try {
			var lastPosition = TFPos.Invalid;
			foreach (var buf in _collectedBuffers) {
				if (buf != null && buf.LastLogPosition > lastPosition)
					lastPosition = buf.LastLogPosition;
			}

			Log.Information("Writing checkpoint {Sequence} for {Projection} at {Position}",
				markerSequence, projectionName, lastPosition);

			var writes = BuildStreamWrites(lastPosition);
			await client.Writing.WriteEvents(writes, requireLeader: true, user);

			Log.Debug("Checkpoint {Sequence} written for {Projection}", markerSequence, projectionName);
		} catch (Exception ex) {
			throw new Exception($"Checkpoint write failed for {projectionName}", ex);
		} finally {
			lock (_lock) {
				Array.Clear(_collectedBuffers);
				_collectedCount = 0;
			}

			_checkpointSemaphore.Release();
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
			if (buffer == null) continue;
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
			if (buffer == null) continue;
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
