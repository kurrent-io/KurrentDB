// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using DotNext.Threading;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.Services.ElectionsService;

internal class FakeEpochManager : IEpochManager {
	public int LastEpochNumber
		=> _epochs.LastOrNone().Convert(static epoch => epoch.EpochNumber).Or(-1);

	private readonly AsyncExclusiveLock _lock = new();

	// use copy-on-write to avoid sync-over-async
	private volatile ImmutableList<EpochRecord> _epochs = ImmutableList<EpochRecord>.Empty;

	public ValueTask Init(CancellationToken token) => ValueTask.CompletedTask;

	public EpochRecord GetLastEpoch() => _epochs.LastOrDefault();

	public ValueTask<IReadOnlyList<EpochRecord>> GetLastEpochs(int maxCount, CancellationToken token) {
		IReadOnlyList<EpochRecord> epochs = _epochs;
		return new((maxCount >= epochs.Count
			? epochs
			: epochs.Skip(_epochs.Count - maxCount)).ToArray());
	}

	public ValueTask<EpochRecord> GetEpochAfter(int epochNumber, bool throwIfNotFound, CancellationToken token) {
		ValueTask<EpochRecord> task;
		try {
			ImmutableList<EpochRecord> epochs = _epochs;
			if (epochs.FirstOrDefault(e => e.EpochNumber == epochNumber) is { } epoch) {
				var index = epochs.IndexOf(epoch);
				epoch = null;
				if (index + 1 < epochs.Count) {
					epoch = epochs[index + 1];
				}
			} else {
				epoch = null;
			}

			if (throwIfNotFound && epoch is null)
				throw new ArgumentOutOfRangeException(nameof(epochNumber), "Epoch not Found");

			task = new(epoch);
		} catch (Exception e) {
			task = ValueTask.FromException<EpochRecord>(e);
		}

		return task;
	}

	public ValueTask<bool> IsCorrectEpochAt(long epochPosition, int epochNumber, Guid epochId, CancellationToken token) {
		ValueTask<bool> task;
		try {
			task = new(_epochs.FirstOrDefault(e => e.EpochNumber == epochNumber) is { } epoch
			           && epoch.EpochNumber == epochNumber
			           && epoch.EpochId == epochId);
		} catch (Exception e) {
			task = ValueTask.FromException<bool>(e);
		}

		return task;
	}

	public ValueTask WriteNewEpoch(int epochNumber, CancellationToken token)
		=> ValueTask.FromException(new NotImplementedException());

	public async ValueTask CacheEpoch(EpochRecord epoch, CancellationToken token) {
		await _lock.AcquireAsync(token);
		try {
			_epochs = _epochs.Add(epoch);
		} finally {
			_lock.Release();
		}
	}

	public ValueTask<EpochRecord> TryTruncateBefore(long position, CancellationToken token)
		=> ValueTask.FromException<EpochRecord>(new NotImplementedException());
}
