// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.StateMachine;

partial class Database : IDatabase {
	IReadOnlyList<IDatabaseNode> IDatabase.Nodes => Nodes;

	public Database AddNode(DatabaseNode node) {
		var copy = new Database(this) { Version = Version + 1UL };
		node.Version = copy.Version;
		copy.Nodes.Add(node);
		return copy;
	}

	public Database UpdateNode(DatabaseNode node, int index) {
		var copy = new Database(this) { Version = Version + 1UL };
		node.Version = copy.Version;
		copy.Nodes[index] = node;
		return copy;
	}

	public Database AppointLeader(ByteString address) => new(this) {
		Version = Version + 1UL,
		Epoch = Epoch + 1UL,
		LeaderAddress = address,
	};
}
