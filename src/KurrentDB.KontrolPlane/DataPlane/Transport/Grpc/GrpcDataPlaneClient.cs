// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace KurrentDB.DataPlane.Transport.Grpc;

/// <summary>
/// Represents gRPC client for the Data Plane.
/// </summary>
public abstract partial class GrpcDataPlaneClient : IDataPlane {
	protected abstract IDisposable CreateChannel(EndPoint address, out CallInvoker invoker);

	public ValueTask<ReplicaState> GetReplicaStateAsync(EndPoint address, CancellationToken token)
		=> GetReplicaStateAsync(GetClient(address), token);

	private static async ValueTask<ReplicaState> GetReplicaStateAsync(DataPlaneNode.DataPlaneNodeClient client,
		CancellationToken token) {
		var response = await client.GetReplicaStateAsync(new Empty(), cancellationToken: token);
		return response.ToEntity();
	}
}
