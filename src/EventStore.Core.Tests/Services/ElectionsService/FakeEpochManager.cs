// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.Services.ElectionsService;

internal class FakeEpochManager : IEpochManager {

	public int LastEpochNumber {
		get {
			lock (_epochs) {
				return _epochs.Any() ? _epochs.Last().EpochNumber : -1;
			}
		}
	}

	private readonly List<EpochRecord> _epochs = new List<EpochRecord>();
	public void Init() {
	}

	public EpochRecord GetLastEpoch() {
		lock (_epochs) {
			return _epochs.LastOrDefault();
		}
	}

	public EpochRecord[] GetLastEpochs(int maxCount) {
		lock (_epochs) {
			if (maxCount >= _epochs.Count) {
				return _epochs.ToArray();
			} else {
				return _epochs.Skip(_epochs.Count - maxCount).ToArray();
			}
		}
	}

	public EpochRecord GetEpochAfter(int epochNumber, bool throwIfNotFound) {
		lock (_epochs) {
			var epoch = _epochs.FirstOrDefault(e => e.EpochNumber == epochNumber);
			if (epoch != null) {
				var index = _epochs.IndexOf(epoch);
				epoch = null;
				if (index + 1 < _epochs.Count) {
					epoch = _epochs[index + 1];
				}
			}

			if (throwIfNotFound && epoch == null)
				throw new ArgumentOutOfRangeException(nameof(epochNumber), "Epoch not Found");
			return epoch;
		}
	}

	public bool IsCorrectEpochAt(long epochPosition, int epochNumber, Guid epochId) {
		lock (_epochs) {
			var epoch = _epochs.FirstOrDefault(e => e.EpochNumber == epochNumber);
			if (epoch == null)
				return false;

			return epoch.EpochPosition == epochPosition &&
				   epoch.EpochId == epochId;
		}
	}

	public void WriteNewEpoch(int epochNumber) {
		throw new NotImplementedException();
	}

	public void CacheEpoch(EpochRecord epoch) {
		lock (_epochs) {
			_epochs.Add(epoch);
		}
	}

	public bool TryTruncateBefore(long position, out EpochRecord epoch) {
		throw new NotImplementedException();
	}
}
