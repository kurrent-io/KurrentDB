// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.Partitioning;

public class PartitionStateCache(int maxCachedPartitions = 4000) {
	private readonly LinkedList<(CheckpointTag Tag, string Partition)> _cacheOrder = [];
	private readonly Dictionary<string, (PartitionState State, CheckpointTag Tag)> _partitionStates = new();
	private CheckpointTag _unlockedBefore = CheckpointTag.Empty;
	private readonly CheckpointTag _zeroPosition = CheckpointTag.Empty;

	public int CachedItemCount { get; private set; }

	public void Initialize() {
		_partitionStates.Clear();
		CachedItemCount = 0;
		_cacheOrder.Clear();
		_unlockedBefore = _zeroPosition;
	}

	public void CacheAndLockPartitionState(string partition, PartitionState data, CheckpointTag at) {
		ArgumentNullException.ThrowIfNull(partition);
		ArgumentNullException.ThrowIfNull(data);
		EnsureCanLockPartitionAt(partition, at);

		_partitionStates[partition] = (data, at);
		CachedItemCount = _partitionStates.Count;

		if (!string.IsNullOrEmpty(partition)) // cached forever - for root state
			_cacheOrder.AddLast((at, partition));
		CleanUp();
	}

	public void CachePartitionState(string partition, PartitionState data) {
		ArgumentNullException.ThrowIfNull(partition);
		ArgumentNullException.ThrowIfNull(data);

		_partitionStates[partition] = (data, _zeroPosition);
		CachedItemCount = _partitionStates.Count;

		_cacheOrder.AddFirst((_zeroPosition, partition));
		CleanUp();
	}

	public PartitionState TryGetAndLockPartitionState(string partition, CheckpointTag lockAt) {
		ArgumentNullException.ThrowIfNull(partition);
		if (!_partitionStates.TryGetValue(partition, out var stateData))
			return null;
		EnsureCanLockPartitionAt(partition, lockAt);
		if (lockAt != null && lockAt <= stateData.Tag)
			throw new InvalidOperationException(
				$"Attempt to relock the '{partition}' partition state locked at the '{stateData.Tag}' position at the earlier position '{lockAt}'");

		_partitionStates[partition] = (stateData.State, lockAt);
		CachedItemCount = _partitionStates.Count;

		if (!string.IsNullOrEmpty(partition)) // cached forever - for root state
			_cacheOrder.AddLast((lockAt, partition));
		CleanUp();
		return stateData.State;
	}

	public PartitionState TryGetPartitionState(string partition) {
		ArgumentNullException.ThrowIfNull(partition);
		return !_partitionStates.TryGetValue(partition, out var stateData) ? null : stateData.Item1;
	}

	public PartitionState GetLockedPartitionState(string partition) {
		if (!_partitionStates.TryGetValue(partition, out var stateData)) {
			throw new InvalidOperationException(
				$"Partition '{partition}' state was requested as locked but it is missing in the cache.");
		}

		if (stateData.Tag != null && stateData.Tag <= _unlockedBefore)
			throw new InvalidOperationException(
				$"Partition '{partition}' state was requested as locked but it is cached as unlocked");
		return stateData.State;
	}

	public void Unlock(CheckpointTag beforeCheckpoint, bool forgetUnlocked = false) {
		_unlockedBefore = beforeCheckpoint;
		CleanUp(removeAllUnlocked: forgetUnlocked);
	}

	private void CleanUp(bool removeAllUnlocked = false) {
		while (removeAllUnlocked || _cacheOrder.Count > maxCachedPartitions * 5
								 || CachedItemCount > maxCachedPartitions) {
			if (_cacheOrder.Count == 0)
				break;
			var top = _cacheOrder.FirstOrDefault();
			if (top.Tag >= _unlockedBefore)
				break; // other entries were locked after the checkpoint (or almost .. order is not very strong)
			_cacheOrder.RemoveFirst();
			if (!_partitionStates.TryGetValue(top.Partition, out var entry))
				continue; // already removed
			if (entry.Tag >= _unlockedBefore)
				continue; // was relocked

			_partitionStates.Remove(top.Partition);
			CachedItemCount = _partitionStates.Count;
		}
	}

	private void EnsureCanLockPartitionAt(string partition, CheckpointTag at) {
		ArgumentNullException.ThrowIfNull(partition);
		if (at == null && partition != "")
			throw new InvalidOperationException("Only the root partition can be locked forever");
		if (partition == "" && at != null)
			throw new InvalidOperationException("Root partition must be locked forever");
		if (at != null && at <= _unlockedBefore)
			throw new InvalidOperationException(
				$"Attempt to lock the '{partition}' partition state at the position '{at}' before the unlocked position '{_unlockedBefore}'");
	}

	public IEnumerable<Tuple<string, PartitionState>> Enumerate() {
		return _partitionStates.Select(v => Tuple.Create(v.Key, v.Value.Item1)).ToList();
	}
}
