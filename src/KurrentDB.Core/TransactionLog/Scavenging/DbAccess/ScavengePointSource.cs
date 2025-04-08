// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace KurrentDB.Core.TransactionLog.Scavenging.DbAccess;

public class ScavengePointSource : IScavengePointSource {
	private readonly ILogger _logger;
	private readonly IODispatcher _ioDispatcher;

	public ScavengePointSource(ILogger logger, IODispatcher ioDispatcher) {
		_logger = logger;
		_ioDispatcher = ioDispatcher;
	}

	public async Task<ScavengePoint> GetLatestScavengePointOrDefaultAsync(
		CancellationToken cancellationToken) {

		_logger.Information("SCAVENGING: Getting latest scavenge point...");

		var readTcs = new TaskCompletionSource<IReadOnlyList<ResolvedEvent>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var endStreamPosition = -1;

		_ioDispatcher.ReadBackward(
			streamId: SystemStreams.ScavengePointsStream,
			fromEventNumber: endStreamPosition,
			maxCount: 1,
			resolveLinks: false,
			principal: SystemAccounts.System,
			action: m => {
				if (m.Result == ReadStreamResult.Success)
					readTcs.TrySetResult(m.Events);
				else if (m.Result == ReadStreamResult.NoStream)
					readTcs.TrySetResult(Array.Empty<ResolvedEvent>());
				else {
					readTcs.TrySetException(new Exception(
						$"Failed to get latest scavenge point: {m.Result}. {m.Error}"));
				}
			},
			timeoutAction: () => {
				readTcs.TrySetException(new Exception(
					"Failed to get latest scavenge point: read timed out"));
			},
			corrId: Guid.NewGuid());

		IReadOnlyList<ResolvedEvent> events;
		using (cancellationToken.Register(() => readTcs.TrySetCanceled())) {
			events = await readTcs.Task;
		}

		if (events is []) {
			_logger.Information("SCAVENGING: No scavenge points exist");
			return default;
		} else if (events.Count is not 1) {
			throw new Exception($"Expected 1 event but got {events.Count}");
		}

		var scavengePointEvent = events[0].Event;

		if (scavengePointEvent.EventType != SystemEventTypes.ScavengePoint)
			throw new Exception($"Last event in {SystemStreams.ScavengePointsStream} is not a scavenge point.");

		var scavengePointPayload = ScavengePointPayload.FromBytes(scavengePointEvent.Data);

		var scavengePoint = new ScavengePoint(
			position: scavengePointEvent.LogPosition,
			eventNumber: scavengePointEvent.EventNumber,
			effectiveNow: scavengePointEvent.TimeStamp,
			threshold: scavengePointPayload.Threshold);

		_logger.Information("SCAVENGING: Latest scavenge point found is {scavengePoint}", scavengePoint);
		return scavengePoint;
	}

	public async Task<ScavengePoint> AddScavengePointAsync(
		long expectedVersion,
		int threshold,
		CancellationToken cancellationToken) {

		_logger.Information("SCAVENGING: Adding new scavenge point #{eventNumber} with threshold {threshold}...",
			expectedVersion + 1, threshold);

		var payload = new ScavengePointPayload {
			Threshold = threshold,
		};

		var writeTcs = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_ioDispatcher.WriteEvent(
			streamId: SystemStreams.ScavengePointsStream,
			expectedVersion: expectedVersion,
			@event: new Event(
				eventId: Guid.NewGuid(),
				eventType: SystemEventTypes.ScavengePoint,
				isJson: true,
				data: payload.ToJsonBytes(),
				metadata: null,
                SchemaInfo.None,
                SchemaInfo.None),
			principal: SystemAccounts.System,
			action: m => {
				if (m.Result == OperationResult.Success) {
					writeTcs.TrySetResult(true);
				} else {
					writeTcs.TrySetException(new Exception(
						$"Failed to add new scavenge point: {m.Result}"));
				}
			}
		);

		try {
			await writeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
		} catch (TimeoutException ex) {
			throw new TimeoutException("Timed out while trying to write a scavenge point", ex);
		}

		_logger.Information("SCAVENGING: Added new scavenge point.");

		// initial chance to replicate (handy if we are follower)
		await Task.Delay(500, cancellationToken);

		const int MaxAttempts = 30;
		var attempt = 0;
		while (true) {
			var scavengePoint = await GetLatestScavengePointOrDefaultAsync(cancellationToken);

			// success
			if (scavengePoint.EventNumber == expectedVersion + 1)
				return scavengePoint;

			// give up
			if (++attempt > MaxAttempts)
				throw new Exception(
					$"Unable to read back new scavenge point {expectedVersion + 1}. " +
					$"This node is most likely significantly behind the leader. " +
					$"Allow it to catch up and then try again. ");

			// retry
			_logger.Information(
				"SCAVENGING: Did not read new scavenge point. " +
				"Found {actual} but expected {expected}. Retrying {attempt}/{maxAttempts}...",
				scavengePoint.EventNumber, expectedVersion + 1, attempt, MaxAttempts);

			await Task.Delay(1000, cancellationToken);
		}
	}
}