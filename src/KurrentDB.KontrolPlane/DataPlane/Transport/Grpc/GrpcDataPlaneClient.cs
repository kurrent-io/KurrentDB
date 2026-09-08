// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace KurrentDB.DataPlane.Transport.Grpc;

/// <summary>
/// Represents gRPC client for the Data Plane.
/// </summary>
public abstract partial class GrpcDataPlaneClient : Disposable, IDataPlane {
	/// <summary>
	/// Creates gRPC communication channel.
	/// </summary>
	/// <param name="address">The address of the gRPC service.</param>
	/// <param name="invoker">The call invoker.</param>
	/// <returns>The network channel that encapsulates the socket.</returns>
	protected abstract IDisposable CreateChannel(EndPoint address, out CallInvoker invoker);

	public ValueTask<ReplicaState> FenceAsync(EndPoint address, ulong currentEpoch, CancellationToken token)
		=> FenceAsync(GetClient(address), currentEpoch, token);

	private static async ValueTask<ReplicaState> FenceAsync(DataPlaneNode.DataPlaneNodeClient client,
		ulong currentEpoch,
		CancellationToken token) {
		var response = await client.FenceAsync(new() { CurrentEpoch = currentEpoch }, cancellationToken: token);
		return response.ToEntity();
	}
}
