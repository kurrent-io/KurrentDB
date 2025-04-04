// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class UpdateGossipProcessor : IRandTestItemProcessor {
	private readonly SendOverGrpcBlockingProcessor _sendOverGrpcProcessor;
	private readonly RandomizedElectionsAndGossipTestCase.CreateUpdatedGossip _createUpdatedGossip;
	private readonly Action<RandTestQueueItem, Message> _enqueue;
	private ElectionsInstance[] _instances;
	private readonly List<RandTestQueueItem> _processedItems;

	private MemberInfo[] _initialGossip;
	private Dictionary<EndPoint, MemberInfo[]> _previousGossip;

	public UpdateGossipProcessor(IEnumerable<ElectionsInstance> allInstances,
		SendOverGrpcBlockingProcessor sendOverGrpcProcessor,
		RandomizedElectionsAndGossipTestCase.CreateUpdatedGossip createUpdatedGossip,
		Action<RandTestQueueItem, Message> enqueue) {
		_sendOverGrpcProcessor = sendOverGrpcProcessor;
		_createUpdatedGossip = createUpdatedGossip;
		_enqueue = enqueue;
		_instances = allInstances.ToArray();

		_processedItems = new List<RandTestQueueItem>();
		ProcessedItems = _processedItems;
	}

	public void SetInitialData(IEnumerable<ElectionsInstance> allInstances,
		IEnumerable<MemberInfo> initialGossip) {
		_instances = allInstances.ToArray();
		_initialGossip = initialGossip.ToArray();
		_previousGossip = _instances.ToDictionary(x => x.EndPoint, v => _initialGossip);
	}

	public IEnumerable<RandTestQueueItem> ProcessedItems { get; private set; }

	public void Process(int iteration, RandTestQueueItem item) {
		var electionsDone = item.Message as ElectionMessage.ElectionsDone;
		if (electionsDone != null) {
			MemberInfo[] previousMembers;
			if (_previousGossip.TryGetValue(item.EndPoint, out previousMembers)) {
				var leaderIndex = Array.FindIndex(previousMembers,
					x => x.Is(electionsDone.Leader.HttpEndPoint));
				if (leaderIndex != -1) {
					var previousLeaderInfo = previousMembers[leaderIndex];
					var leaderEndPoint = previousLeaderInfo.HttpEndPoint;

					previousMembers[leaderIndex] =
						MemberInfo.ForVNode(previousLeaderInfo.InstanceId, DateTime.UtcNow, VNodeState.Leader,
							previousLeaderInfo.IsAlive, leaderEndPoint, null, leaderEndPoint, null, leaderEndPoint, null, 0, 0,
							-1, 0, 0, -1, -1, Guid.Empty, 0, false);
				}
			}
		}

		var updatedGossip = _createUpdatedGossip(iteration, item, _instances, _initialGossip, _previousGossip);
		if (updatedGossip != null) {
			if (updatedGossip.Length > _instances.Length)
				throw new InvalidDataException(
					"Gossip should not contain more items than there are servers in the cluster.");

			_processedItems.Add(item);

			foreach (var memberInfo in updatedGossip) {
				_sendOverGrpcProcessor.RegisterEndpointToSkip(memberInfo.ExternalTcpEndPoint, !memberInfo.IsAlive);
			}

			var updateGossipMessage = new GossipMessage.GossipUpdated(new ClusterInfo(updatedGossip));

			_enqueue(item, updateGossipMessage);
			_previousGossip[item.EndPoint] = updatedGossip;

			var leader = updateGossipMessage.ClusterInfo.Members.FirstOrDefault(x => x.IsAlive && x.State == VNodeState.Leader);
			if (leader == null)
				_enqueue(item, new ElectionMessage.StartElections());
		}
	}

	public void LogMessages() {
	}
}
