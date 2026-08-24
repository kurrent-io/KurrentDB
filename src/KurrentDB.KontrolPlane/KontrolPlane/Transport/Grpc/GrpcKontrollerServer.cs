// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Threading;
using Google.Protobuf;
using Grpc.Core;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

/// <summary>
/// Represents server-side of the Kontroller.
/// </summary>
/// <param name="kontroller">The Kontroller instance.</param>
public abstract class GrpcKontrollerServer(IKontroller kontroller) : Kontroller.KontrollerBase {
	/// <summary>
	/// Converts KPlane node address to the address that can be used to access KPlane
	/// node via gRPC.
	/// </summary>
	/// <param name="nodeEndPoint">The node address.</param>
	/// <returns>gRPC endpoint address that can be used to access the KPlane node.</returns>
	protected abstract EndPoint GetApiEndPoint(EndPoint nodeEndPoint);

	public sealed override async Task<RenewLeaderAppointmentResponse> RenewLeaderAppointment(RenewLeaderAppointmentRequest request, ServerCallContext context) {
		var response = new RenewLeaderAppointmentResponse();
		try {
			response.Success = await kontroller.RenewLeaderAppointmentAsync(request.DatabaseId,
				request.Address.ToEndPoint(),
				request.Epoch,
				new(request.InstanceId.Span),
				context.CancellationToken);
		} catch (LeadershipRequiredException) {
			// the current node is not a leader
			response.KontrollerLeader = GetApiEndPoint(await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString();
			response.Success = false;
		}

		return response;
	}

	public sealed override async Task AnnounceDatabaseNode(AnnouncementRequest request, IServerStreamWriter<AnnouncementResponse> responseStream, ServerCallContext context) {
		// announcement
		AnnouncementResponse response;
		try {
			await kontroller.TryAddDatabaseNodeAsync(request.NodeInfo.ToEntity(), context.CancellationToken);
		} catch (LeadershipRequiredException) {
			// the current node is not a leader
			response = new() {
				KontrollerLeader = GetApiEndPoint(await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString(),
				Cluster = null,
			};

			response.KontrollerNodes.Add(KontrollerNodes);
			await responseStream.WriteAsync(response);
			return;
		}

		// streaming
		var leadershipToken = kontroller.LeadershipToken;
		var tokenSource = CancellationToken.Combine([context.CancellationToken, leadershipToken]);
		var enumerator = kontroller
			.ListenDatabaseAsync(request.NodeInfo.DatabaseId, tokenSource.Token)
			.GetAsyncEnumerator();

		try {
			while (await enumerator.MoveNextAsync()) {
				response = new() {
					Cluster = new(enumerator.Current),
					KontrollerLeader = ByteString.Empty,
				};

				response.KontrollerNodes.Add(KontrollerNodes);
				await responseStream.WriteAsync(response);
			}
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, leadershipToken)) {
			// the current node is not a leader
			response = new AnnouncementResponse {
				KontrollerLeader = GetApiEndPoint(await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString(),
				Cluster = null,
			};

			response.KontrollerNodes.Add(KontrollerNodes);
			await responseStream.WriteAsync(response);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			// restore canceled token
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await enumerator.DisposeAsync();
			tokenSource.Dispose();
		}
	}

	public sealed override async Task<ResignResponse> ResignLeader(ResignRequest request, ServerCallContext context) {
		var response = new ResignResponse();
		try {
			response.Successful = await kontroller.ResignDatabaseLeaderAsync(request.DatabaseId,
				request.HasEpoch ? request.Epoch : null,
				context.CancellationToken);
			response.KontrollerLeader = ByteString.Empty;
		} catch (LeadershipRequiredException) {
			// the current node is not a leader
			response.Successful = false;
			response.KontrollerLeader = GetApiEndPoint(await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString();
		}

		return response;
	}

	private IEnumerable<ByteString> KontrollerNodes
		=> kontroller.Nodes.Select(GetApiEndPoint).Select(EndPointExtensions.ToByteString);
}
