// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext;
using Grpc.Core;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

/// <summary>
/// Provides access to the Kontrol Plane via gRPC protocol.
/// </summary>
public abstract partial class GrpcKontrolPlaneClient : Disposable, IKontrolPlane {
	/// <summary>
	/// Sets a statically known list of Kontroller nodes.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The set is empty.</exception>
	public required IReadOnlySet<EndPoint> KontrolPlaneNodes {
		init => _kontrollerNodes = value.Count > 0 ? [.. value] : throw new ArgumentOutOfRangeException(nameof(value));
	}

	/// <summary>
	/// Gets or sets timeout for <see cref="RenewLeaderAppointmentAsync"/> or <see cref="ResignLeaderAsync"/> underlying gRPC
	/// calls.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
	public TimeSpan UnaryCallTimeout {
		get;
		init => field = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
	} = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Creates gRPC communication channel.
	/// </summary>
	/// <param name="address">The address of the gRPC service.</param>
	/// <param name="invoker">The call invoker.</param>
	/// <returns>The network channel that encapsulates the socket.</returns>
	protected abstract IDisposable CreateChannel(EndPoint address, out CallInvoker invoker);

	/// <inheritdoc cref="IKontrolPlane.AnnounceNodeAsync"/>
	public async IAsyncEnumerable<KontrolPlane.DatabaseCluster> AnnounceNodeAsync(KontrolPlane.DatabaseNode node, [EnumeratorCancellation] CancellationToken token = default) {
		for (var currentAddress = CurrentAddress;; token.ThrowIfCancellationRequested()) {
			var entry = GetOrCreateClient(currentAddress);

			var call = entry.Client.AnnounceDatabaseNode(new() { NodeInfo = new(node) }, cancellationToken: token);
			try {
				// Outer loop for reconnections
				// Inner loop for enumerating database cluster changes
				for (;; token.ThrowIfCancellationRequested()) {
					try {
						if (!await call.ResponseStream.MoveNext())
							break;
					} catch (RpcException e) when (e.StatusCode
						                               is StatusCode.DeadlineExceeded
						                               or StatusCode.Unavailable
						                               or StatusCode.Cancelled) {
						currentAddress = MarkAsUnavailable(currentAddress, newAddress: null);
						break;
					}

					// we have a result, update list of KPlane nodes
					var response = call.ResponseStream.Current;
					_kontrollerNodes = [.. response.KontrollerNodes.Select(EndPointExtensions.ToEndPoint)];

					// KPlane informed us about a new KPlane leader, switch to it
					if (!response.KontrollerLeader.IsEmpty) {
						currentAddress = MarkAsUnavailable(currentAddress, response.KontrollerLeader.ToEndPoint());
						break;
					}

					yield return response.Cluster.ToEntity();
				}
			} finally {
				call.Dispose();
				entry.Release();
			}
		}
	}

	/// <inheritdoc cref="IKontrolPlane.RenewLeaderAppointmentAsync"/>
	public async Task<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint nodeAddress, ulong nodeEpoch, CancellationToken token = default) {
		// Loop is needed to reach the KPlane leader
		for (var currentAddress = CurrentAddress;; token.ThrowIfCancellationRequested()) {
			var entry = GetOrCreateClient(currentAddress);

			try {
				var response = await entry.Client.RenewLeaderAppointmentAsync(new() {
					DatabaseId = databaseId,
					Epoch = nodeEpoch,
					Address = nodeAddress.ToByteString()
				}, cancellationToken: token);

				// We've got a response from the leader
				if (response.KontrollerLeader.IsEmpty)
					return response.Success;

				// Otherwise, change the address and try again
				currentAddress = MarkAsUnavailable(currentAddress, response.KontrollerLeader.ToEndPoint());
			} catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
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
			} catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
				currentAddress = MarkAsUnavailable(currentAddress, newAddress: null);
			} finally {
				entry.Release();
			}
		}
	}
}
