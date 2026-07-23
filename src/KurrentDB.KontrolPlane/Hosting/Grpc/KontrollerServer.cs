// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using Google.Protobuf;
using Grpc.Core;

namespace KurrentDB.KontrolPlane.Hosting.Grpc;

/// <summary>
/// Represents server-side of the Kontroller.
/// </summary>
/// <param name="kontroller">The Kontroller instance.</param>
public sealed class KontrollerServer(IKontroller kontroller) : Kontroller.KontrollerBase {
	public override async Task<KeepAliveResponse> KeepAlive(KeepAliveRequest request, ServerCallContext context) {
		var response = new KeepAliveResponse();
		try {
			response.Success = await kontroller.RenewLeaderAppointmentAsync(request.DatabaseId, request.Address.ToEndPoint(), request.Epoch,
				context.CancellationToken);
		} catch (LeadershipRequiredException) {
			// the current node is not a leader
			response.KontrollerLeader = (await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString();
			response.Success = false;
		}

		return response;
	}

	public override async Task Announce(AnnouncementRequest request, IServerStreamWriter<AnnouncementResponse> responseStream, ServerCallContext context) {
		// announcement
		try {
			await kontroller.AddOrUpdateDatabaseNodeAsync(request.NodeInfo.ToEntity(), context.CancellationToken);
		} catch (LeadershipRequiredException) {
			// the current node is not a leader
			await responseStream.WriteAsync(new() {
				AppointmentDuration = kontroller.AppointmentDuration.Ticks,
				KontrollerLeader = (await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString(),
				Cluster = null,
			});

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
				await responseStream.WriteAsync(new() {
					AppointmentDuration = kontroller.AppointmentDuration.Ticks,
					Cluster = new(enumerator.Current),
					KontrollerLeader = ByteString.Empty,
				});
			}
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, leadershipToken)) {
			// the current node is not a leader
			await responseStream.WriteAsync(new() {
				AppointmentDuration = kontroller.AppointmentDuration.Ticks,
				KontrollerLeader = (await kontroller.WaitForLeaderAsync(context.CancellationToken)).ToByteString(),
				Cluster = null,
			});
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			// restore canceled token
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await enumerator.DisposeAsync();
			tokenSource.Dispose();
		}
	}
}
