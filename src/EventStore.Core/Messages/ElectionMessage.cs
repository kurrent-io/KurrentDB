// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class ElectionMessage {
	[DerivedMessage(CoreMessage.Election)]
	public partial class StartElections : Message {
		public override string ToString() {
			return "---- StartElections";
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class ViewChange : Message {
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;

		public readonly int AttemptedView;

		public ViewChange(Guid serverId,
			EndPoint serverHttpEndPoint,
			int attemptedView) {
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;

			AttemptedView = attemptedView;
		}

		public ViewChange(ElectionMessageDto.ViewChangeDto dto) {
			AttemptedView = dto.AttemptedView;
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
		}

		public override string ToString() {
			return string.Format("---- ViewChange: attemptedView {0}, serverId {1}, serverHttp {2}",
				AttemptedView, ServerId, ServerHttpEndPoint);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class ViewChangeProof : Message {
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;
		public readonly int InstalledView;

		public ViewChangeProof(Guid serverId, EndPoint serverHttpEndPoint, int installedView) {
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
			InstalledView = installedView;
		}

		public ViewChangeProof(ElectionMessageDto.ViewChangeProofDto dto) {
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
			InstalledView = dto.InstalledView;
		}

		public override string ToString() {
			return string.Format("---- ViewChangeProof: serverId {0}, serverHttp {1}, installedView {2}",
				ServerId, ServerHttpEndPoint, InstalledView);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class SendViewChangeProof : Message {
		public override string ToString() {
			return string.Format("---- SendViewChangeProof");
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class ElectionsTimedOut : Message {
		public readonly int View;

		public ElectionsTimedOut(int view) {
			View = view;
		}

		public override string ToString() {
			return string.Format("---- ElectionsTimedOut: view {0}", View);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class Prepare : Message {
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;
		public readonly int View;

		public Prepare(Guid serverId, EndPoint serverHttpEndPoint, int view) {
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
			View = view;
		}

		public Prepare(ElectionMessageDto.PrepareDto dto) {
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
			View = dto.View;
		}

		public override string ToString() {
			return string.Format("---- Prepare: serverId {0}, serverHttp {1}, view {2}", ServerId,
				ServerHttpEndPoint, View);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class PrepareOk : Message {
		public readonly int View;
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;
		public readonly int EpochNumber;
		public readonly long EpochPosition;
		public readonly Guid EpochId;
		public readonly Guid EpochLeaderInstanceId;
		public readonly long LastCommitPosition;
		public readonly long WriterCheckpoint;
		public readonly long ChaserCheckpoint;
		public readonly int NodePriority;
		public readonly ClusterInfo ClusterInfo;

		public PrepareOk(int view,
			Guid serverId,
			EndPoint serverHttpEndPoint,
			int epochNumber,
			long epochPosition,
			Guid epochId,
			Guid epochLeaderInstanceId,
			long lastCommitPosition,
			long writerCheckpoint,
			long chaserCheckpoint,
			int nodePriority,
			ClusterInfo clusterInfo) {
			View = view;
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
			EpochNumber = epochNumber;
			EpochPosition = epochPosition;
			EpochId = epochId;
			EpochLeaderInstanceId = epochLeaderInstanceId;
			LastCommitPosition = lastCommitPosition;
			WriterCheckpoint = writerCheckpoint;
			ChaserCheckpoint = chaserCheckpoint;
			NodePriority = nodePriority;
			ClusterInfo = clusterInfo;
		}

		public PrepareOk(ElectionMessageDto.PrepareOkDto dto) {
			View = dto.View;
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
			EpochNumber = dto.EpochNumber;
			EpochPosition = dto.EpochPosition;
			EpochId = dto.EpochId;
			EpochLeaderInstanceId = dto.EpochLeaderInstanceId;
			LastCommitPosition = dto.LastCommitPosition;
			WriterCheckpoint = dto.WriterCheckpoint;
			ChaserCheckpoint = dto.ChaserCheckpoint;
			NodePriority = dto.NodePriority;
			ClusterInfo = dto.ClusterInfo;
		}

		public override string ToString() {
			return string.Format(
				"---- PrepareOk: view {0}, serverId {1}, serverHttp {2}, epochNumber {3}, " +
				"epochPosition {4}, epochId {5}, epochLeaderInstanceId {6:B}, lastCommitPosition {7}, writerCheckpoint {8}, chaserCheckpoint {9}, nodePriority: {10}, clusterInfo: {11}",
				View, ServerId, ServerHttpEndPoint, EpochNumber,
				EpochPosition, EpochId, EpochLeaderInstanceId, LastCommitPosition, WriterCheckpoint, ChaserCheckpoint, NodePriority, ClusterInfo);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class Proposal : Message {
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;
		public readonly Guid LeaderId;
		public readonly EndPoint LeaderHttpEndPoint;

		public readonly int View;
		public readonly int EpochNumber;
		public readonly long EpochPosition;
		public readonly Guid EpochId;
		public readonly Guid EpochLeaderInstanceId;
		public readonly long LastCommitPosition;
		public readonly long WriterCheckpoint;
		public readonly long ChaserCheckpoint;
		public readonly int NodePriority;

		public Proposal(Guid serverId, EndPoint serverHttpEndPoint, Guid leaderId, EndPoint leaderHttpEndPoint,
			int view, int epochNumber, long epochPosition, Guid epochId, Guid epochLeaderInstanceId,
			long lastCommitPosition, long writerCheckpoint, long chaserCheckpoint, int nodePriority) {
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
			LeaderId = leaderId;
			LeaderHttpEndPoint = leaderHttpEndPoint;
			View = view;
			EpochNumber = epochNumber;
			EpochPosition = epochPosition;
			EpochId = epochId;
			EpochLeaderInstanceId = epochLeaderInstanceId;
			LastCommitPosition = lastCommitPosition;
			WriterCheckpoint = writerCheckpoint;
			ChaserCheckpoint = chaserCheckpoint;
			NodePriority = nodePriority;
		}

		public Proposal(ElectionMessageDto.ProposalDto dto) {
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
			LeaderId = dto.LeaderId;
			LeaderHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.LeaderHttpAddress),
				dto.LeaderHttpPort);
			View = dto.View;
			EpochNumber = dto.EpochNumber;
			EpochPosition = dto.EpochPosition;
			EpochId = dto.EpochId;
			EpochLeaderInstanceId = dto.EpochLeaderInstanceId;
			LastCommitPosition = dto.LastCommitPosition;
			WriterCheckpoint = dto.WriterCheckpoint;
			ChaserCheckpoint = dto.ChaserCheckpoint;
			NodePriority = dto.NodePriority;
		}

		public override string ToString() {
			return string.Format(
				"---- Proposal: serverId {0}, serverHttp {1}, leaderId {2}, leaderHttp {3}, "
				+ "view {4}, lastCommitCheckpoint {5}, writerCheckpoint {6}, chaserCheckpoint {7}, epoch {8}@{9}:{10:B} (L={11:B}), NodePriority {12}",
				ServerId, ServerHttpEndPoint, LeaderId, LeaderHttpEndPoint,
				View, LastCommitPosition, WriterCheckpoint, ChaserCheckpoint,
				EpochNumber, EpochPosition, EpochId, EpochLeaderInstanceId, NodePriority);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class Accept : Message {
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;
		public readonly Guid LeaderId;
		public readonly EndPoint LeaderHttpEndPoint;
		public readonly int View;

		public Accept(Guid serverId, EndPoint serverHttpEndPoint, Guid leaderId, EndPoint leaderHttpEndPoint,
			int view) {
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
			LeaderId = leaderId;
			LeaderHttpEndPoint = leaderHttpEndPoint;

			View = view;
		}

		public Accept(ElectionMessageDto.AcceptDto dto) {
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
			LeaderId = dto.LeaderId;
			LeaderHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.LeaderHttpAddress),
				dto.LeaderHttpPort);
			View = dto.View;
		}

		public override string ToString() {
			return string.Format(
				"---- Accept: serverId {0}, serverHttp {1}, leaderId {2}, leaderHttp {3}, view {4}",
				ServerId, ServerHttpEndPoint, LeaderId, LeaderHttpEndPoint, View);
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class LeaderIsResigning : Message {
		public readonly Guid LeaderId;
		public readonly EndPoint LeaderHttpEndPoint;

		public LeaderIsResigning(Guid leaderId, EndPoint leaderHttpEndPoint) {
			LeaderId = leaderId;
			LeaderHttpEndPoint = leaderHttpEndPoint;
		}

		public LeaderIsResigning(ElectionMessageDto.LeaderIsResigningDto dto) {
			LeaderId = dto.LeaderId;
			LeaderHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.LeaderHttpAddress),
				dto.LeaderHttpPort);
		}

		public override string ToString() {
			return $"---- LeaderIsResigning: serverId {LeaderId}";
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class LeaderIsResigningOk : Message {
		public readonly Guid LeaderId;
		public readonly EndPoint LeaderHttpEndPoint;
		public readonly Guid ServerId;
		public readonly EndPoint ServerHttpEndPoint;

		public LeaderIsResigningOk(ElectionMessageDto.LeaderIsResigningOkDto dto) {
			LeaderId = dto.LeaderId;
			LeaderHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.LeaderHttpAddress),
				dto.LeaderHttpPort);
			ServerId = dto.ServerId;
			ServerHttpEndPoint = new IPEndPoint(IPAddress.Parse(dto.ServerHttpAddress),
				dto.ServerHttpPort);
		}

		public LeaderIsResigningOk(Guid leaderId, EndPoint leaderHttpEndPoint, Guid serverId, EndPoint serverHttpEndPoint) {
			LeaderId = leaderId;
			LeaderHttpEndPoint = leaderHttpEndPoint;
			ServerId = serverId;
			ServerHttpEndPoint = serverHttpEndPoint;
		}

		public override string ToString() {
			return $"---- LeaderIsResigningOk: serverId {ServerId}";
		}
	}

	[DerivedMessage(CoreMessage.Election)]
	public partial class ElectionsDone : Message {
		public readonly int InstalledView;
		public readonly int ProposalNumber;
		public readonly MemberInfo Leader;

		public ElectionsDone(int installedView, int proposalNumber, MemberInfo leader) {
			Ensure.Nonnegative(installedView, "installedView");
			Ensure.NotNull(leader, "leader");
			InstalledView = installedView;
			Leader = leader;
			ProposalNumber = proposalNumber;
		}

		public override string ToString() {
			return $"---- ElectionsDone: installedView {InstalledView}, proposal number {ProposalNumber}, leader {Leader}";
		}
	}
}
