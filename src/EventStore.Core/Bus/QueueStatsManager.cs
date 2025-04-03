// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.TransactionLog.Checkpoint;

namespace EventStore.Core.Bus;

public class QueueStatsManager {
#if DEBUG
	private IReadOnlyCheckpoint _writerCheckpoint;
	private IReadOnlyCheckpoint _chaserCheckpoint;
	private ConcurrentDictionary<QueueStatsCollector, bool> _queueStatsCollectors = new ConcurrentDictionary<QueueStatsCollector, bool>();
#endif
	public QueueStatsManager() {
	}

	public QueueStatsCollector CreateQueueStatsCollector(string name, string groupName = null) {
		var statsCollector = new QueueStatsCollector(name, groupName);
#if DEBUG
		_queueStatsCollectors.AddOrUpdate(statsCollector, (statsCollector) => true,
			(statsCollector, curValue) => throw new Exception("This should never happen"));
#endif
		return statsCollector;
	}

#if DEBUG
	private void WaitStop(int multiplier = 1) {
		var counter = 0;
		foreach (var kvp in _queueStatsCollectors) {
			var queueStatsCollector = kvp.Key;
			while (!queueStatsCollector.IsStopped()) {
				Console.WriteLine($"Waiting for STOP state for queue {queueStatsCollector.Name}...");
				counter++;
				if (counter > 150 * multiplier)
					throw new ApplicationException($"Infinite WaitStop() loop for queue {queueStatsCollector.Name}?");
				Thread.Sleep(100);
			}
		}
	}

	public void WaitIdle(bool waitForCheckpoints = true, bool waitForNonEmptyTf = false,
		int multiplier = 1) {
		var counter = 0;

		var singlePass = false;
		do {
			singlePass = true;
			foreach (var kvp in _queueStatsCollectors) {
				var queueStatsCollector = kvp.Key;
				while (!queueStatsCollector.IsIdle()) {
					Console.WriteLine($"Waiting for IDLE state for queue {queueStatsCollector.Name}...");
					counter++;
					singlePass = false;
					if (counter > 150 * multiplier)
						throw new ApplicationException($"Infinite WaitIdle() loop for queue: {queueStatsCollector.Name}?");
					Thread.Sleep(100);
				}
			}
		} while (!singlePass);

		var successes = 0;
		do {
			if ((waitForCheckpoints && AreCheckpointsDifferent()) ||
			    (waitForNonEmptyTf && _writerCheckpoint.Read() == 0)) {
				Console.WriteLine("Waiting for IDLE state on checkpoints...");
				counter++;
				if (counter > 150 * multiplier)
					throw new ApplicationException("Infinite WaitIdle() loop on checkpoints?");
				Thread.Sleep(100);
			} else {
				successes++;
			}
		} while (successes < 2);
	}

	private bool AreCheckpointsDifferent() {
		return _writerCheckpoint!=null && _chaserCheckpoint!=null && _writerCheckpoint.ReadNonFlushed() != _chaserCheckpoint.Read();
	}

	public void InitializeCheckpoints(
		IReadOnlyCheckpoint writerCheckpoint,
		IReadOnlyCheckpoint chaserCheckpoint) {
		_writerCheckpoint = writerCheckpoint;
		_chaserCheckpoint = chaserCheckpoint;
	}
#endif
}
