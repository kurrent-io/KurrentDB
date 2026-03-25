// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class CheckpointCoordinator(int partitionCount, string projectionName, IPublisher bus, ClaimsPrincipal user) {
	private static readonly ILogger Log = Serilog.Log.ForContext<CheckpointCoordinator>();

	private readonly string _checkpointStreamId = $"$projections-{projectionName}-checkpoint";

	private readonly OutputBuffer[] _collectedBuffers = new OutputBuffer[partitionCount];
	private ulong _currentMarkerSequence;
	private int _collectedCount;
	private readonly SemaphoreSlim _checkpointSemaphore = new(1, 1);
	private readonly Lock _lock = new();

	public async Task ReportPartitionCheckpoint(int partitionIndex, ulong markerSequence, OutputBuffer buffer) {
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

			var (streamIds, expectedVersions, events, streamIndexes) = BuildMultiStreamWrite(lastPosition);

			var envelope = new TcsEnvelope<ClientMessage.WriteEventsCompleted>();
			var corrId = Guid.NewGuid();

			bus.Publish(new ClientMessage.WriteEvents(
				internalCorrId: corrId,
				correlationId: corrId,
				envelope: envelope,
				requireLeader: true,
				eventStreamIds: streamIds.ToArray(),
				expectedVersions: expectedVersions.ToArray(),
				events: events.ToArray(),
				eventStreamIndexes: streamIndexes.ToArray(),
				user: user));

			var result = await envelope.Task;
			if (result.Result != OperationResult.Success) {
				throw new Exception($"Checkpoint write failed for {projectionName}: {result.Result} — {result.Message}");
			}

			Log.Debug("Checkpoint {Sequence} written for {Projection}", markerSequence, projectionName);
		} finally {
			lock (_lock) {
				Array.Clear(_collectedBuffers);
				_collectedCount = 0;
			}

			_checkpointSemaphore.Release();
		}
	}

	private (List<string>, List<long>, List<Event>, List<int>) BuildMultiStreamWrite(TFPos checkpointPosition) {
		var streamIds = new List<string>();
		var expectedVersions = new List<long>();
		var events = new List<Event>();
		var streamIndexes = new List<int>();
		var streamIndexMap = new Dictionary<string, int>();

		// 1. Checkpoint event
		var checkpointData = Encoding.UTF8.GetBytes(
			$"{{\"commitPosition\":{checkpointPosition.CommitPosition},\"preparePosition\":{checkpointPosition.PreparePosition}}}");
		var cpIdx = GetOrAddStream(_checkpointStreamId, ExpectedVersion.Any);
		events.Add(new Event(Guid.NewGuid(), "$ProjectionCheckpoint", true, checkpointData, isPropertyMetadata: false, null));
		streamIndexes.Add(cpIdx);

		// 2. Emitted events
		foreach (var buffer in _collectedBuffers) {
			if (buffer == null) continue;
			foreach (var emitted in buffer.EmittedEvents) {
				var e = emitted.Event;
				var idx = GetOrAddStream(e.StreamId, ExpectedVersion.Any);
				var data = e.Data != null ? Helper.UTF8NoBom.GetBytes(e.Data) : null;
				var metadata = SerializeExtraMetadata(e);
				events.Add(new Event(e.EventId, e.EventType, e.IsJson, data, isPropertyMetadata: false, metadata));
				streamIndexes.Add(idx);
			}
		}

		// 3. State updates
		foreach (var buffer in _collectedBuffers) {
			if (buffer == null) continue;
			foreach (var (_, (streamName, stateJson, expVer)) in buffer.DirtyStates) {
				var idx = GetOrAddStream(streamName, expVer);
				var stateData = Encoding.UTF8.GetBytes(stateJson);
				events.Add(new Event(Guid.NewGuid(), "Result", true, stateData, isPropertyMetadata: false, null));
				streamIndexes.Add(idx);
			}
		}

		return (streamIds, expectedVersions, events, streamIndexes);

		int GetOrAddStream(string streamName, long expectedVersion) {
			if (!streamIndexMap.TryGetValue(streamName, out var idx)) {
				idx = streamIds.Count;
				streamIds.Add(streamName);
				expectedVersions.Add(expectedVersion);
				streamIndexMap[streamName] = idx;
			}

			return idx;
		}
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
