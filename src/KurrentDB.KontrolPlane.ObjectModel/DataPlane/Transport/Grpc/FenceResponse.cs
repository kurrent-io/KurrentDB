// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.DataPlane.Transport.Grpc;

partial class FenceResponse {
	public FenceResponse(in ReplicaState state) {
		Epoch = state.Epoch;
		ChaserCheckpoint = state.ChaserCheckpoint;
		WriterCheckpoint = state.WriterCheckpoint;
		Priority = state.Priority;
		InstanceId = ByteString.CopyFrom(state.InstanceId.ToByteArray());
	}

	public ReplicaState ToEntity() => new() {
		Epoch = Epoch,
		ChaserCheckpoint = ChaserCheckpoint,
		WriterCheckpoint = WriterCheckpoint,
		Priority = Priority,
		InstanceId = InstanceId.Span is { Length: 16 } instanceId ? new(instanceId) : Guid.Empty,
	};
}
