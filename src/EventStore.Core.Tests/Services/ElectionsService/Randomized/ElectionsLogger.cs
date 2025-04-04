// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class ElectionsLogger : IRandTestItemProcessor {
	public IEnumerable<RandTestQueueItem> ProcessedItems {
		get { return _items; }
	}

	public IEnumerable<Message> Messages {
		get { return _items.Select(x => x.Message); }
	}

	private readonly List<RandTestQueueItem> _items = new List<RandTestQueueItem>();

	public void Process(int iteration, RandTestQueueItem item) {
		_items.Add(item);
	}

	public void LogMessages() {
		Console.WriteLine("There were a total of {0} messages in this run.", ProcessedItems.Count());

		foreach (var it in ProcessedItems) {
			Console.WriteLine(it);

			var gossip = it.Message as Messages.GossipMessage.GossipUpdated;
			if (gossip != null) {
				Console.WriteLine("=== gsp on {0}", it.EndPoint);
				Console.WriteLine(gossip.ClusterInfo.ToString().Replace("; ", Environment.NewLine));
				Console.WriteLine("===");
			}

			var done = it.Message as Messages.ElectionMessage.ElectionsDone;
			if (done != null) {
				Console.WriteLine("=== leader on {0}: {1}", it.EndPoint, done.Leader);
			}
		}
	}
}
