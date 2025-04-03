// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public class PartitionStateCache {
	private readonly int _maxCachedPartitions;

	private readonly LinkedList<Tuple<CheckpointTag, string>> _cacheOrder =
		new LinkedList<Tuple<CheckpointTag, string>>();

	private readonly Dictionary<string, Tuple<PartitionState, CheckpointTag>> _partitionStates =
		new Dictionary<string, Tuple<PartitionState, CheckpointTag>>();

	private int _cachedItemCount;

	private CheckpointTag _unlockedBefore;
	private readonly CheckpointTag _zeroPosition;

	public PartitionStateCache(int maxCachedPartitions = 4000) {
		_zeroPosition = CheckpointTag.Empty;
		_unlockedBefore = CheckpointTag.Empty;
		_maxCachedPartitions = maxCachedPartitions;
	}

	public int CachedItemCount {
		get { return _cachedItemCount; }
	}

	public void Initialize() {
		_partitionStates.Clear();
		_cachedItemCount = 0;

		_cacheOrder.Clear();
		_unlockedBefore = _zeroPosition;
	}

	public void CacheAndLockPartitionState(string partition, PartitionState data, CheckpointTag at) {
		if (partition == null) throw new ArgumentNullException("partition");
		if (data == null) throw new ArgumentNullException("data");
		EnsureCanLockPartitionAt(partition, at);

		_partitionStates[partition] = Tuple.Create(data, at);
		_cachedItemCount = _partitionStates.Count;

		if (!string.IsNullOrEmpty(partition)) // cached forever - for root state
			_cacheOrder.AddLast(Tuple.Create(at, partition));
		CleanUp();
	}

	public void CachePartitionState(string partition, PartitionState data) {
		if (partition == null) throw new ArgumentNullException("partition");
		if (data == null) throw new ArgumentNullException("data");

		_partitionStates[partition] = Tuple.Create(data, _zeroPosition);
		_cachedItemCount = _partitionStates.Count;

		_cacheOrder.AddFirst(Tuple.Create(_zeroPosition, partition));
		CleanUp();
	}

	public PartitionState TryGetAndLockPartitionState(string partition, CheckpointTag lockAt) {
		if (partition == null) throw new ArgumentNullException("partition");
		Tuple<PartitionState, CheckpointTag> stateData;
		if (!_partitionStates.TryGetValue(partition, out stateData))
			return null;
		EnsureCanLockPartitionAt(partition, lockAt);
		if (lockAt != null && lockAt <= stateData.Item2)
			throw new InvalidOperationException(
				string.Format(
					"Attempt to relock the '{0}' partition state locked at the '{1}' position at the earlier position '{2}'",
					partition, stateData.Item2, lockAt));

		_partitionStates[partition] = Tuple.Create(stateData.Item1, lockAt);
		_cachedItemCount = _partitionStates.Count;

		if (!string.IsNullOrEmpty(partition)) // cached forever - for root state
			_cacheOrder.AddLast(Tuple.Create(lockAt, partition));
		CleanUp();
		return stateData.Item1;
	}

	public PartitionState TryGetPartitionState(string partition) {
		if (partition == null) throw new ArgumentNullException("partition");
		Tuple<PartitionState, CheckpointTag> stateData;
		if (!_partitionStates.TryGetValue(partition, out stateData))
			return null;
		return stateData.Item1;
	}

	public PartitionState GetLockedPartitionState(string partition) {
		Tuple<PartitionState, CheckpointTag> stateData;
		if (!_partitionStates.TryGetValue(partition, out stateData)) {
			throw new InvalidOperationException(
				string.Format(
					"Partition '{0}' state was requested as locked but it is missing in the cache.", partition));
		}

		if (stateData.Item2 != null && stateData.Item2 <= _unlockedBefore)
			throw new InvalidOperationException(
				string.Format(
					"Partition '{0}' state was requested as locked but it is cached as unlocked", partition));
		return stateData.Item1;
	}

	public void Unlock(CheckpointTag beforeCheckpoint, bool forgetUnlocked = false) {
		_unlockedBefore = beforeCheckpoint;
		CleanUp(removeAllUnlocked: forgetUnlocked);
	}

	private void CleanUp(bool removeAllUnlocked = false) {
		while (removeAllUnlocked || _cacheOrder.Count > _maxCachedPartitions * 5
		                         || CachedItemCount > _maxCachedPartitions) {
			if (_cacheOrder.Count == 0)
				break;
			Tuple<CheckpointTag, string> top = _cacheOrder.FirstOrDefault();
			if (top.Item1 >= _unlockedBefore)
				break; // other entries were locked after the checkpoint (or almost .. order is not very strong)
			_cacheOrder.RemoveFirst();
			Tuple<PartitionState, CheckpointTag> entry;
			if (!_partitionStates.TryGetValue(top.Item2, out entry))
				continue; // already removed
			if (entry.Item2 >= _unlockedBefore)
				continue; // was relocked

			_partitionStates.Remove(top.Item2);
			_cachedItemCount = _partitionStates.Count;
		}
	}

	private void EnsureCanLockPartitionAt(string partition, CheckpointTag at) {
		if (partition == null) throw new ArgumentNullException("partition");
		if (at == null && partition != "")
			throw new InvalidOperationException("Only the root partition can be locked forever");
		if (partition == "" && at != null)
			throw new InvalidOperationException("Root partition must be locked forever");
		if (at != null && at <= _unlockedBefore)
			throw new InvalidOperationException(
				string.Format(
					"Attempt to lock the '{0}' partition state at the position '{1}' before the unlocked position '{2}'",
					partition, at, _unlockedBefore));
	}

	public IEnumerable<Tuple<string, PartitionState>> Enumerate() {
		return _partitionStates.Select(v => Tuple.Create(v.Key, v.Value.Item1)).ToList();
	}
}
