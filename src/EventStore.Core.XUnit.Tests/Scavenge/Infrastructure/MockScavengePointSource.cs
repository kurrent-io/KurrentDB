// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

// The real ScavengePointSource is a bit awkward to use in the tests because it uses the IODispatcher
// Also even if we plumbed in the necessary components to the test to get that to work, it would
// still have to write the scavengepoint to the chunks and also to the index in order to behave in
// reasonable way.
public class MockScavengePointSource : IScavengePointSource {
	private readonly ILogRecord[][] _log;
	private readonly DateTime _effectiveNow;
	private readonly List<ScavengePoint> _added;

	public MockScavengePointSource(
		DbResult dbResult,
		DateTime effectiveNow,
		List<ScavengePoint> added) {

		_log = dbResult.Recs;
		_effectiveNow = effectiveNow;
		_added = added;
	}

	public Task<ScavengePoint> GetLatestScavengePointOrDefaultAsync(
		CancellationToken cancellationToken) {

		ScavengePoint scavengePoint = default;

		foreach (var record in AllRecords()) {
			if (record is PrepareLogRecord prepare &&
				prepare.EventType == SystemEventTypes.ScavengePoint) {

				var payload = ScavengePointPayload.FromBytes(prepare.Data);

				scavengePoint = new ScavengePoint(
					position: record.LogPosition,
					eventNumber: prepare.ExpectedVersion + 1,
					effectiveNow: prepare.TimeStamp,
					threshold: payload.Threshold);
			}
		}

		return Task.FromResult(scavengePoint);
	}

	public async Task<ScavengePoint> AddScavengePointAsync(
		long expectedVersion,
		int threshold,
		CancellationToken cancellationToken) {

		var latestScavengePoint = await GetLatestScavengePointOrDefaultAsync(cancellationToken);
		var actualVersion = latestScavengePoint != null
			? latestScavengePoint.EventNumber
			: -1;

		if (actualVersion != expectedVersion) {
			throw new InvalidOperationException(
				$"wrong version number {expectedVersion} vs {actualVersion}");
		}

		// this is only used in a specific way, when CancelOnNewScavengePoint is called.
		// the scavenge point isn't used for a scavenge, we just assert that the scavenge
		// created it properly
		var scavengePoint = new ScavengePoint(
			position: -1,
			eventNumber: expectedVersion + 1,
			effectiveNow: _effectiveNow,
			threshold: threshold);

		_added.Add(scavengePoint);
		throw new OperationCanceledException();
	}

	private IEnumerable<ILogRecord> AllRecords() {
		for (var chunkIndex = 0; chunkIndex < _log.Length; chunkIndex++) {
			for (var recordIndex = 0; recordIndex < _log[chunkIndex].Length; recordIndex++) {
				yield return _log[chunkIndex][recordIndex];
			}
		}
	}
}
