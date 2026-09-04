// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext;
using Google.Protobuf;
using Grpc.Core;
using Serilog;
using static System.Threading.Timeout;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

/// <summary>
/// Provides access to the Kontrol Plane via gRPC protocol.
/// </summary>
public abstract partial class GrpcKontrolPlaneClient : Disposable, IKontrolPlane {
	/// <summary>
	/// How long to wait before reconnecting the announce stream, so that an unreachable Kontrol Plane
	/// does not turn the reconnection loop into a spin.
	/// </summary>
	private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// Sets a statically known list of Kontroller nodes.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The set is empty.</exception>
	public required IReadOnlySet<EndPoint> KontrolPlaneNodes {
		init => _kontrollerNodes = value.Count > 0 ? [.. value] : throw new ArgumentOutOfRangeException(nameof(value));
	}

	/// <summary>
	/// Creates gRPC communication channel.
	/// </summary>
	/// <param name="address">The address of the gRPC service.</param>
	/// <param name="invoker">The call invoker.</param>
	/// <returns>The network channel that encapsulates the socket.</returns>
	protected abstract IDisposable CreateChannel(EndPoint address, out CallInvoker invoker);

	/// <inheritdoc cref="IKontrolPlane.AnnounceNodeAsync"/>
	public async IAsyncEnumerable<KontrolPlane.DatabaseCluster> AnnounceNodeAsync(KontrolPlane.DatabaseNode node, [EnumeratorCancellation] CancellationToken token = default) {
		for (var currentAddress = _kontrollerNodes[0];; token.ThrowIfCancellationRequested()) {
			Log.Error("AnnounceNodeAsync top of main loop");
			var entry = CreateClient(currentAddress);

			var call = entry.Client.AnnounceDatabaseNode(new() { NodeInfo = new(node) }, cancellationToken: token);
			var redirected = false;
			try {
				// Outer loop for reconnections
				// Inner loop for enumerating database cluster changes
				for (;; token.ThrowIfCancellationRequested()) {
					Log.Error("AnnounceNodeAsync top of inner loop");
					try {
						if (!await call.ResponseStream.MoveNext())
							break;
					} catch (RpcException) {
						currentAddress = NextAddress(currentAddress);
						break;
					}

					// we have a result, update list of KPlane nodes
					var response = call.ResponseStream.Current;
					_kontrollerNodes = [.. response.KontrollerNodes.Select(EndPointExtensions.ToEndPoint)];

					// KPlane informed us about a new KPlane leader, switch to it
					if (!response.KontrollerLeader.IsEmpty) {
						currentAddress = response.KontrollerLeader.ToEndPoint();
						redirected = true;
						break;
					}

					yield return response.Cluster.ToEntity();
				}
			} finally {
				call.Dispose();
				entry.Release();
			}

			// Following a redirect to the Kontrol Plane leader is progress, so reconnect straight away.
			if (!redirected)
				await Task.Delay(ReconnectDelay, token);
		}
	}

	/// <inheritdoc cref="IKontrolPlane.RenewLeaderAppointmentAsync"/>
	public async Task<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint nodeAddress, ulong nodeEpoch, Guid instanceId, CancellationToken token = default) {
		// Loop is needed to reach the KPlane leader
		for (var currentAddress = CurrentAddress;; token.ThrowIfCancellationRequested()) {
			var entry = GetOrCreateClient(currentAddress);

			try {
				var response = await entry.Client.RenewLeaderAppointmentAsync(new() {
					DatabaseId = databaseId,
					Epoch = nodeEpoch,
					Address = nodeAddress.ToByteString(),
					InstanceId = ByteString.CopyFrom(instanceId.ToByteArray()),
				}, cancellationToken: token);

				// We've got a response from the leader
				if (response.KontrollerLeader.IsEmpty)
					return response.Success;

				// Otherwise, change the address and try again
				currentAddress = MarkAsUnavailable(currentAddress, response.KontrollerLeader.ToEndPoint());
			} catch (RpcException) {
				currentAddress = MarkAsUnavailable(currentAddress, newAddress: null);
			} finally {
				entry.Release();
			}
		}
	}

	public async Task<bool> ResignLeaderAsync(string databaseId, ulong? epoch, CancellationToken token = default) {
		// Loop is needed to reach the KPlane leader
		for (var currentAddress = CurrentAddress;; token.ThrowIfCancellationRequested()) {
			var entry = GetOrCreateClient(currentAddress);

			try {
				var request = new ResignRequest() { DatabaseId = databaseId };
				if (epoch.HasValue) {
					request.Epoch = epoch.GetValueOrDefault();
				} else {
					request.ClearEpoch();
				}

				var response = await entry.Client.ResignLeaderAsync(request, cancellationToken: token);

				// We've got a response from the leader
				if (response.KontrollerLeader.IsEmpty)
					return response.Successful;

				// Otherwise, change the address and try again
				currentAddress = MarkAsUnavailable(currentAddress, response.KontrollerLeader.ToEndPoint());
			} catch (RpcException) {
				currentAddress = MarkAsUnavailable(currentAddress, newAddress: null);
			} finally {
				entry.Release();
			}
		}
	}
}
