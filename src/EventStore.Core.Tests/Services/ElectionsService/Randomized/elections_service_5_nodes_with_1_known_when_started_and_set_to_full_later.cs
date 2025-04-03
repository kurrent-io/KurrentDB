// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

[TestFixture, Ignore("Not sure the finish criteria is correct")]
public class elections_service_5_nodes_with_1_known_when_started_and_set_to_full_later {
	private RandomizedElectionsAndGossipTestCase _randomCase;

	[SetUp]
	public void SetUp() {
		_randomCase = new RandomizedElectionsAndGossipTestCase(ElectionParams.MaxIterationCount,
			instancesCnt: 5,
			httpLossProbability: 0.3,
			httpDupProbability: 0.3,
			httpMaxDelay: 20,
			timerMinDelay: 100,
			timerMaxDelay: 200,
			createInitialGossip: CreateInitialGossip,
			createUpdatedGossip: CreateUpdatedGossip
		);

		_randomCase.Init();
	}

	private MemberInfo[] CreateInitialGossip(ElectionsInstance instance, ElectionsInstance[] allInstances) {
		return new[] {
			MemberInfo.ForVNode(instance.InstanceId, DateTime.UtcNow, VNodeState.Unknown, true,
				instance.EndPoint, null, instance.EndPoint, null, instance.EndPoint, null, 0, 0,
				-1, 0, 0, -1, -1, Guid.Empty, 0, false)
		};
	}

	private MemberInfo[] CreateUpdatedGossip(int iteration,
		RandTestQueueItem item,
		ElectionsInstance[] instances,
		MemberInfo[] initialGossip,
		Dictionary<EndPoint, MemberInfo[]> previousGossip) {
		if (iteration == 1 || (iteration % 100 != (item.EndPoint.GetPort() % 1000) && _randomCase.Next(100) < 30))
			return null;

		if (previousGossip[item.EndPoint].Length < 5) {
			return instances.Select((x, i) =>
					MemberInfo.ForVNode(x.InstanceId, DateTime.UtcNow, VNodeState.Unknown, true,
						x.EndPoint, null, x.EndPoint, null, x.EndPoint, null, 0, 0,
						-1, 0, 0, -1, -1, Guid.Empty, 0, false))
				.ToArray();
		}

		return null;
	}

	[Test, Category("LongRunning"), Category("Network")]
	public void should_complete_successfully([Range(0, ElectionParams.TestRunCount - 1)]
		int run) {
		var success = _randomCase.Run();
		if (!success)
			_randomCase.Logger.LogMessages();

		Console.WriteLine("There were a total of {0} messages in this run.",
			_randomCase.Logger.ProcessedItems.Count());
		Console.WriteLine("There were {0} GossipUpdated messages in this run.",
			_randomCase.Logger.ProcessedItems.Count(x => x.Message is GossipMessage.GossipUpdated));

		Assert.True(success);
	}
}
