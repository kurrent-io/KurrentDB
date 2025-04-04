// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.RequestManager;

public class CommitSource :
IHandle<ReplicationTrackingMessage.IndexedTo>,
IHandle<ReplicationTrackingMessage.ReplicatedTo> {
	private readonly ConcurrentDictionary<long, List<Action>> _notifyReplicated = new ConcurrentDictionary<long, List<Action>>();
	private readonly ConcurrentDictionary<long, List<Action>> _notifyIndexed = new ConcurrentDictionary<long, List<Action>>();
	private long _replicatedPosition;
	private long _indexedPosition;

	public long ReplicationPosition => _replicatedPosition;
	public long IndexedPosition => _indexedPosition;

	public void Handle(ReplicationTrackingMessage.ReplicatedTo message) {
		Interlocked.Exchange(ref _replicatedPosition, message.LogPosition);
		Notify(_notifyReplicated, message.LogPosition);
	}

	public void Handle(ReplicationTrackingMessage.IndexedTo message) {
		Interlocked.Exchange(ref _indexedPosition, message.LogPosition);
		Notify(_notifyIndexed, message.LogPosition);
	}

	public void NotifyFor(long position, Action target, CommitLevel level = CommitLevel.Indexed) {
		long currentPosition;
		ConcurrentDictionary<long, List<Action>> notificationDictionary;
		switch (level) {
			case CommitLevel.Replicated:
				currentPosition = Interlocked.Read(ref _replicatedPosition);
				notificationDictionary = _notifyReplicated;
				break;
			case CommitLevel.Indexed:
				currentPosition = Interlocked.Read(ref _indexedPosition);
				notificationDictionary = _notifyIndexed;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(level), level, null);
		}
		if (currentPosition >= position) { target(); }
		if (!notificationDictionary.TryGetValue(position, out var actionList)) {
			actionList = new List<Action> { target };
			notificationDictionary.TryAdd(position, actionList);
		} else {
			lock (actionList) {
				actionList.Add(target);
			}
		}
	}
	private void Notify(ConcurrentDictionary<long, List<Action>> dictionary, long logPosition) {
		if (dictionary.IsEmpty) { return; }
		long[] positions;
		lock (dictionary) {
			positions = dictionary.Keys.ToArray();
		}
		Array.Sort(positions);
		var actionList = new List<Action>();
		for (int i = 0; i < positions.Length && positions[i] <= logPosition; i++) {
			if (dictionary.TryRemove(positions[i], out var actions) && actions != null) {
				actionList.AddRange(actions);
			}
		}
		actionList?.ForEach(a => { try { a?.Invoke(); } catch { /*ignore*/ } });
	}
}
