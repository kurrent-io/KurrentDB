// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.Hosting.Grpc;

partial class DatabaseCluster {
	public DatabaseCluster(KontrolPlane.DatabaseCluster cluster) {
		DatabaseLeader = cluster.LeaderAddress?.ToByteString() ?? ByteString.Empty;
		Epoch = cluster.Epoch;
		foreach (var databaseNode in cluster.Nodes) {
			Nodes.Add(new DatabaseNode(databaseNode));
		}
	}
}
