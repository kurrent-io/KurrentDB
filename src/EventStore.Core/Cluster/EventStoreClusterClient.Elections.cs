// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Cluster;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Grpc;
using EndPoint = System.Net.EndPoint;
using GossipEndPoint = EventStore.Cluster.EndPoint;

namespace EventStore.Core.Cluster;

public partial class EventStoreClusterClient {
	public void SendViewChange(ElectionMessage.ViewChange msg, EndPoint destinationEndpoint, DateTime deadline) {
		SendViewChangeAsync(msg.ServerId, msg.ServerHttpEndPoint, msg.AttemptedView, deadline).ContinueWith(r => {
			if (r.Exception != null) {
				Log.Information(r.Exception, "View Change Send Failed to {Server}", destinationEndpoint);
			}
		});
	}

	public void SendViewChangeProof(ElectionMessage.ViewChangeProof msg, EndPoint destinationEndpoint,
		DateTime deadline) {
		SendViewChangeProofAsync(msg.ServerId, msg.ServerHttpEndPoint, msg.InstalledView, deadline).ContinueWith(
			r => {
				if (r.Exception != null) {
					Log.Information(r.Exception, "View Change Proof Send Failed to {Server}",
						destinationEndpoint);
				}
			});
	}

	public void SendPrepare(ElectionMessage.Prepare msg, EndPoint destinationEndpoint, DateTime deadline) {
		SendPrepareAsync(msg.ServerId, msg.ServerHttpEndPoint, msg.View, deadline).ContinueWith(r => {
			if (r.Exception != null) {
				Log.Information(r.Exception, "Prepare Send Failed to {Server}", destinationEndpoint);
			}
		});
	}

	public void SendPrepareOk(ElectionMessage.PrepareOk prepareOk, EndPoint destinationEndpoint,
		DateTime deadline) {
		SendPrepareOkAsync(prepareOk.View, prepareOk.ServerId, prepareOk.ServerHttpEndPoint, prepareOk.EpochNumber,
				prepareOk.EpochPosition, prepareOk.EpochId, prepareOk.EpochLeaderInstanceId, prepareOk.LastCommitPosition,
				prepareOk.WriterCheckpoint,
				prepareOk.ChaserCheckpoint, prepareOk.NodePriority, prepareOk.ClusterInfo, deadline)
			.ContinueWith(r => {
				if (r.Exception != null) {
					Log.Information(r.Exception, "Prepare OK Send Failed to {Server}",
						destinationEndpoint);
				}
			});
	}

	public void SendProposal(ElectionMessage.Proposal proposal, EndPoint destinationEndpoint, DateTime deadline) {
		SendProposalAsync(proposal.ServerId, proposal.ServerHttpEndPoint, proposal.LeaderId,
				proposal.LeaderHttpEndPoint,
				proposal.View, proposal.EpochNumber, proposal.EpochPosition, proposal.EpochId,proposal.EpochLeaderInstanceId,
				proposal.LastCommitPosition, proposal.WriterCheckpoint, proposal.ChaserCheckpoint,
				proposal.NodePriority,
				deadline)
			.ContinueWith(r => {
				if (r.Exception != null) {
					Log.Information(r.Exception, "Proposal Send Failed to {Server}",
						destinationEndpoint);
				}
			});
	}

	public void SendAccept(ElectionMessage.Accept accept, EndPoint destinationEndpoint, DateTime deadline) {
		SendAcceptAsync(accept.ServerId, accept.ServerHttpEndPoint, accept.LeaderId, accept.LeaderHttpEndPoint,
				accept.View, deadline)
			.ContinueWith(r => {
				if (r.Exception != null) {
					Log.Information(r.Exception, "Accept Send Failed to {Server}", destinationEndpoint);
				}
			});
	}

	public void SendLeaderIsResigning(ElectionMessage.LeaderIsResigning resigning, EndPoint destinationEndpoint,
		DateTime deadline) {
		SendLeaderIsResigningAsync(resigning.LeaderId, resigning.LeaderHttpEndPoint, deadline).ContinueWith(r => {
			if (r.Exception != null) {
				Log.Information(r.Exception, "Leader is Resigning Send Failed to {Server}", destinationEndpoint);
			}
		});
	}

	public void SendLeaderIsResigningOk(ElectionMessage.LeaderIsResigningOk resigningOk,
		EndPoint destinationEndpoint, DateTime deadline) {
		SendLeaderIsResigningOkAsync(resigningOk.LeaderId, resigningOk.LeaderHttpEndPoint,
			resigningOk.ServerId, resigningOk.ServerHttpEndPoint, deadline).ContinueWith(r => {
			if (r.Exception != null) {
				Log.Information(r.Exception, "Leader is Resigning Ok Send Failed to {Server}", destinationEndpoint);
			}
		});
	}

	private async Task SendViewChangeAsync(Guid serverId, EndPoint serverHttpEndPoint, int attemptedView,
		DateTime deadline) {
		var request = new ViewChangeRequest {
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			AttemptedView = attemptedView
		};
		await _electionsClient.ViewChangeAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendViewChangeProofAsync(Guid serverId, EndPoint serverHttpEndPoint, int installedView,
		DateTime deadline) {
		var request = new ViewChangeProofRequest {
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			InstalledView = installedView
		};
		await _electionsClient.ViewChangeProofAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendPrepareAsync(Guid serverId, EndPoint serverHttpEndPoint, int view, DateTime deadline) {
		var request = new PrepareRequest {
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			View = view
		};
		await _electionsClient.PrepareAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendPrepareOkAsync(int view, Guid serverId, EndPoint serverHttpEndPoint, int epochNumber,
		long epochPosition, Guid epochId, Guid epochLeaderInstanceId, long lastCommitPosition, long writerCheckpoint, long chaserCheckpoint,
		int nodePriority, ClusterInfo clusterInfo, DateTime deadline) {
		var request = new PrepareOkRequest {
			View = view,
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			EpochNumber = epochNumber,
			EpochPosition = epochPosition,
			EpochId = Uuid.FromGuid(epochId).ToDto(),
			EpochLeaderInstanceId = Uuid.FromGuid(epochLeaderInstanceId).ToDto(),
			LastCommitPosition = lastCommitPosition,
			WriterCheckpoint = writerCheckpoint,
			ChaserCheckpoint = chaserCheckpoint,
			NodePriority = nodePriority,
			ClusterInfo = ClusterInfo.ToGrpcClusterInfo(clusterInfo)
		};
		await _electionsClient.PrepareOkAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendProposalAsync(Guid serverId, EndPoint serverHttpEndPoint, Guid leaderId,
		EndPoint leaderHttp, int view, int epochNumber, long epochPosition, Guid epochId, Guid epochLeaderInstanceId,
		long lastCommitPosition, long writerCheckpoint, long chaserCheckpoint, int nodePriority,
		DateTime deadline) {
		var request = new ProposalRequest {
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			LeaderId = Uuid.FromGuid(leaderId).ToDto(),
			LeaderHttp = new GossipEndPoint(leaderHttp.GetHost(), (uint)leaderHttp.GetPort()),
			View = view,
			EpochNumber = epochNumber,
			EpochPosition = epochPosition,
			EpochId = Uuid.FromGuid(epochId).ToDto(),
			EpochLeaderInstanceId = Uuid.FromGuid(epochLeaderInstanceId).ToDto(),
			LastCommitPosition = lastCommitPosition,
			WriterCheckpoint = writerCheckpoint,
			ChaserCheckpoint = chaserCheckpoint,
			NodePriority = nodePriority
		};
		await _electionsClient.ProposalAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendAcceptAsync(Guid serverId, EndPoint serverHttpEndPoint, Guid leaderId,
		EndPoint leaderHttp, int view, DateTime deadline) {
		var request = new AcceptRequest {
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
			LeaderId = Uuid.FromGuid(leaderId).ToDto(),
			LeaderHttp = new GossipEndPoint(leaderHttp.GetHost(), (uint)leaderHttp.GetPort()),
			View = view
		};
		await _electionsClient.AcceptAsync(request);
		_electionsClient.Accept(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendLeaderIsResigningAsync(Guid leaderId, EndPoint leaderHttp, DateTime deadline) {
		var request = new LeaderIsResigningRequest {
			LeaderId = Uuid.FromGuid(leaderId).ToDto(),
			LeaderHttp = new GossipEndPoint(leaderHttp.GetHost(), (uint)leaderHttp.GetPort()),
		};
		await _electionsClient.LeaderIsResigningAsync(request, deadline: deadline.ToUniversalTime());
	}

	private async Task SendLeaderIsResigningOkAsync(Guid leaderId, EndPoint leaderHttp,
		Guid serverId, EndPoint serverHttpEndPoint, DateTime deadline) {
		var request = new LeaderIsResigningOkRequest {
			LeaderId = Uuid.FromGuid(leaderId).ToDto(),
			LeaderHttp = new GossipEndPoint(leaderHttp.GetHost(), (uint)leaderHttp.GetPort()),
			ServerId = Uuid.FromGuid(serverId).ToDto(),
			ServerHttp = new GossipEndPoint(serverHttpEndPoint.GetHost(), (uint)serverHttpEndPoint.GetPort()),
		};
		await _electionsClient.LeaderIsResigningOkAsync(request, deadline: deadline.ToUniversalTime());
	}
}
