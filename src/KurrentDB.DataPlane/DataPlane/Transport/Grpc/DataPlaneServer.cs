// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace KurrentDB.DataPlane.Transport.Grpc;

/// <summary>
/// Represents Data Plane node gRPC server.
/// </summary>
/// <param name="host"></param>
public sealed class DataPlaneServer(DatabaseNodeHost host) : DataPlaneNode.DataPlaneNodeBase {
	public override async Task<GetReplicaStateResponse> GetReplicaState(Empty request, ServerCallContext context) {
		return new(await host.GetReplicaStateAsync(context.CancellationToken));
	}
}
