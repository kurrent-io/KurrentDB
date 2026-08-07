// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

partial class DatabaseCluster {
	public DatabaseCluster(KontrolPlane.DatabaseCluster cluster) {
		DatabaseLeader = cluster.LeaderAddress?.ToByteString() ?? ByteString.Empty;
		Epoch = cluster.Epoch;
		Id = cluster.Id;
		Description = cluster.Description;
		LeaderAppointmentDuration = cluster.LeaderAppointmentDuration.Ticks;
		foreach (var databaseNode in cluster.Nodes) {
			Nodes.Add(new DatabaseNode(databaseNode));
		}
	}

	public KontrolPlane.DatabaseCluster ToEntity() => new() {
		Id = Id,
		Description = Description,
		LeaderAppointmentDuration = new(LeaderAppointmentDuration),
		LeaderAddress = DatabaseLeader.IsEmpty ? null : DatabaseLeader.ToEndPoint(),
		Epoch = Epoch,
		Nodes = [.. Nodes.Select(static n => n.ToEntity())]
	};
}
