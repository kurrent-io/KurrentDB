// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane.Transport.Grpc;

partial class GetReplicaStateResponse {
	public ReplicaState ToEntity() => new() {
		Epoch = Epoch,
		ChaserCheckpoint = ChaserCheckpoint,
		WriterCheckpoint = WriterCheckpoint,
		Priority = Priority,
	};
}
