// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class RandomizedElectionsAndGossipTestCase : RandomizedElectionsTestCase {
	public delegate MemberInfo[] CreateInitialGossip(ElectionsInstance instance, ElectionsInstance[] allInstances);

	public delegate MemberInfo[] CreateUpdatedGossip(int iteration,
		RandTestQueueItem item,
		ElectionsInstance[] instances,
		MemberInfo[] initialGossip,
		Dictionary<EndPoint, MemberInfo[]> previousGossip);

	private readonly CreateInitialGossip _createInitialGossip;
	private readonly CreateUpdatedGossip _createUpdatedGossip;

	private readonly SendOverGrpcBlockingProcessor _sendOverGrpcBlockingProcessor;
	private readonly UpdateGossipProcessor _updateGossipProcessor;

	public RandomizedElectionsAndGossipTestCase(int maxIterCnt,
		int instancesCnt,
		double httpLossProbability,
		double httpDupProbability,
		int httpMaxDelay,
		int timerMinDelay,
		int timerMaxDelay,
		CreateInitialGossip createInitialGossip,
		CreateUpdatedGossip createUpdatedGossip,
		int? rndSeed = null)
		: base(maxIterCnt,
			instancesCnt,
			httpLossProbability,
			httpDupProbability,
			httpMaxDelay,
			timerMinDelay,
			timerMaxDelay,
			rndSeed) {
		_createInitialGossip = createInitialGossip;
		_createUpdatedGossip = createUpdatedGossip;

		_sendOverGrpcBlockingProcessor = new SendOverGrpcBlockingProcessor(Rnd,
			Runner,
			HttpLossProbability,
			HttpDupProbability,
			HttpMaxDelay);

		_updateGossipProcessor = new UpdateGossipProcessor(new ElectionsInstance[0],
			_sendOverGrpcBlockingProcessor,
			_createUpdatedGossip,
			Enqueue
		);
	}

	private void Enqueue(RandTestQueueItem item, Message message) {
		Runner.Enqueue(item.EndPoint, message, item.Bus, 10);
	}

	protected override IRandTestFinishCondition GetFinishCondition() {
		return new ElectionsProgressCondition(InstancesCnt);
	}

	protected override IRandTestItemProcessor[] GetAdditionalProcessors() {
		return new IRandTestItemProcessor[] {_updateGossipProcessor};
	}

	protected override GossipMessage.GossipUpdated GetInitialGossipFor(ElectionsInstance instance,
		List<ElectionsInstance> allInstances) {
		var members = _createInitialGossip(instance, allInstances.ToArray());
		_updateGossipProcessor.SetInitialData(allInstances, members);

		return new GossipMessage.GossipUpdated(new ClusterInfo(members));
	}

	protected override SendOverGrpcProcessor GetSendOverHttpProcessor() {
		return _sendOverGrpcBlockingProcessor;
	}

	public int Next(int maxValue) {
		return Rnd.Next(maxValue);
	}
}
