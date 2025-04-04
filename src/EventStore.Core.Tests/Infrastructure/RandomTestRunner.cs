// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.DataStructures;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Infrastructure;

public class RandomTestRunner {
	private readonly int _maxIterCnt;
	private readonly PairingHeap<RandTestQueueItem> _queue;

	private int _iter;
	private int _curLogicalTime;
	private int _globalMsgId;

	public RandomTestRunner(int maxIterCnt) {
		_maxIterCnt = maxIterCnt;
		_queue = new PairingHeap<RandTestQueueItem>(new GlobalQueueItemComparer());
	}

	public bool Run(IRandTestFinishCondition finishCondition, params IRandTestItemProcessor[] processors) {
		Ensure.NotNull(finishCondition, "finishCondition");

		while (++_iter <= _maxIterCnt && _queue.Count > 0) {
			var item = _queue.DeleteMin();
			_curLogicalTime = item.LogicalTime;
			foreach (var processor in processors) {
				processor.Process(_iter, item);
			}

			finishCondition.Process(_iter, item);
			if (finishCondition.Done)
				break;

			item.Bus.Publish(item.Message);
		}

		return finishCondition.Success;
	}

	public void Enqueue(EndPoint endPoint, Message message, IPublisher bus, int timeDelay = 1) {
		System.Diagnostics.Debug.Assert(timeDelay >= 1);
		_queue.Add(new RandTestQueueItem(_curLogicalTime + timeDelay, _globalMsgId++, endPoint, message, bus));
	}

	private class GlobalQueueItemComparer : IComparer<RandTestQueueItem> {
		public int Compare(RandTestQueueItem x, RandTestQueueItem y) {
			if (x.LogicalTime == y.LogicalTime)
				return x.GlobalId - y.GlobalId;
			return x.LogicalTime - y.LogicalTime;
		}
	}
}
