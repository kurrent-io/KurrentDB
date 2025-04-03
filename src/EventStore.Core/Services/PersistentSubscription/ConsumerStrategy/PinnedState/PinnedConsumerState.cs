// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy.PinnedState;

class PinnedConsumerState {
	private const int MaxBucketCount = 1024;

	public PinnedConsumerState() {
		Version = -1;
		Assignments = new BucketAssignment[MaxBucketCount];
		Nodes = new List<Node>();
	}

	public int Version { get; set; }

	public BucketAssignment[] Assignments { get; set; }

	public IList<Node> Nodes { get; set; }

	public int AssignmentCount { get; set; }

	public int TotalCapacity { get; set; }

	public int AvailableCapacity {
		get {
			var cap = 0;
			for (int index = 0; index < Nodes.Count; index++) {
				var node = Nodes[index];
				if (node.State == Node.NodeState.Connected) {
					cap += node.MaximumInFlightMessages - node.Client.InflightMessages;
				}
			}

			return cap;
		}
	}

	public void DisconnectNode(Guid nodeId) {
		var node = Nodes.FirstOrDefault(_ => _.NodeId == nodeId);

		if (node == null) {
			throw new ApplicationException(
				"ClientRemoved was called for a client the consumer strategy didn't have.");
		}

		if (node.State == Node.NodeState.Disconnected)
			throw new InvalidOperationException();

		node.State = Node.NodeState.Disconnected;

		AssignmentCount -= node.AssignmentCount;
		if (AssignmentCount < 0)
			throw new InvalidOperationException();

		TotalCapacity -= node.MaximumInFlightMessages;
		if (TotalCapacity < 0)
			throw new InvalidOperationException();

		for (int i = 0; i < Assignments.Length; i++) {
			if (Assignments[i].NodeId == nodeId) {
				Assignments[i].State = BucketAssignment.BucketState.Disconnected;
				Assignments[i].InFlightCount = 0;
			}
		}
	}

	public void AddNode(Node newNode) {
		Nodes.Add(newNode);
		TotalCapacity += newNode.MaximumInFlightMessages;

		var clientCount = Nodes.Count(_ => _.State == Node.NodeState.Connected);

		var maxBalancedClientAssignmentCount = (int)Math.Ceiling(AssignmentCount / (decimal)clientCount);

		var reassignments = new List<uint>();

		foreach (var existingClient in Nodes) {
			if (existingClient == newNode || existingClient.State == Node.NodeState.Disconnected) {
				continue;
			}

			if (existingClient.AssignmentCount > maxBalancedClientAssignmentCount) {
				var assignmentsToMove = Assignments
					.Select((node, bucket) => Tuple.Create(node, bucket))
					.Where(_ => _.Item1.NodeId == existingClient.NodeId &&
					            _.Item1.State == BucketAssignment.BucketState.Assigned)
					.OrderBy(_ => _.Item1.InFlightCount) // Take buckets without inflight messages first.
					.Take(existingClient.AssignmentCount - maxBalancedClientAssignmentCount);

				foreach (var assignment in assignmentsToMove) {
					reassignments.Add((uint)assignment.Item2);
				}
			}
		}

		foreach (var reassignment in reassignments) {
			ApplyBucketAssigned(reassignment, newNode.NodeId);
		}

		Clean();
	}

	public void EventRemoved(Guid nodeId, uint assignmentId) {
		if (Assignments[assignmentId].NodeId == nodeId) {
			Assignments[assignmentId].InFlightCount--;
		}
	}

	public void RecordEventSent(uint bucket) {
		Assignments[bucket].InFlightCount++;
	}

	public void AssignBucket(uint bucket) {
		var node = ChooseClient();
		ApplyBucketAssigned(bucket, node.NodeId);
	}

	private void Clean() {
		var oldNodes = Nodes.Where(_ => _.AssignmentCount == 0 && _.State == Node.NodeState.Disconnected).ToList();
		foreach (var oldNode in oldNodes) {
			Nodes.Remove(oldNode);
		}
	}

	private void ApplyBucketAssigned(uint bucketId, Guid newNodeId) {
		if (Assignments[bucketId].State != BucketAssignment.BucketState.Assigned) {
			AssignmentCount++;
		}

		if (Assignments[bucketId].State != BucketAssignment.BucketState.Unassigned) {
			Assignments[bucketId].Node.AssignmentCount--;
		}

		Assignments[bucketId].State = BucketAssignment.BucketState.Assigned;
		Assignments[bucketId].NodeId = newNodeId;
		Assignments[bucketId].Node = Nodes.First(_ => _.NodeId == newNodeId);
		Assignments[bucketId].Node.AssignmentCount++;
	}

	private Node ChooseClient() {
		Node minAssignedNode = null;
		foreach (var node in Nodes.Where(_ => _.State == Node.NodeState.Connected)) {
			if (minAssignedNode == null || node.AssignmentCount < minAssignedNode.AssignmentCount) {
				minAssignedNode = node;
			}
		}

		return minAssignedNode;
	}
}
