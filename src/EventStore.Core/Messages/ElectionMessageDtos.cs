// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;

namespace EventStore.Core.Messages;

public static class ElectionMessageDto {
	public class ViewChangeDto {
		public Guid ServerId { get; set; }
		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }

		public int AttemptedView { get; set; }

		public ViewChangeDto() {
		}

		public ViewChangeDto(ElectionMessage.ViewChange message) {
			ServerId = message.ServerId;
			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();

			AttemptedView = message.AttemptedView;
		}
	}

	public class ViewChangeProofDto {
		public Guid ServerId { get; set; }
		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }

		public int InstalledView { get; set; }

		public ViewChangeProofDto() {
		}

		public ViewChangeProofDto(ElectionMessage.ViewChangeProof message) {
			ServerId = message.ServerId;
			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();

			InstalledView = message.InstalledView;
		}
	}

	public class PrepareDto {
		public Guid ServerId { get; set; }
		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }

		public int View { get; set; }

		public PrepareDto() {
		}

		public PrepareDto(ElectionMessage.Prepare message) {
			ServerId = message.ServerId;
			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();

			View = message.View;
		}
	}

	public class PrepareOkDto {
		public Guid ServerId { get; set; }
		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }

		public int View { get; set; }

		public int EpochNumber { get; set; }
		public long EpochPosition { get; set; }
		public Guid EpochId { get; set; }
		public Guid EpochLeaderInstanceId { get; set; }
		public long LastCommitPosition { get; set; }
		public long WriterCheckpoint { get; set; }
		public long ChaserCheckpoint { get; set; }

		public int NodePriority { get; set; }
		public ClusterInfo ClusterInfo { get; set; }

		public PrepareOkDto() {
		}

		public PrepareOkDto(ElectionMessage.PrepareOk message) {
			ServerId = message.ServerId;
			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();

			View = message.View;

			EpochNumber = message.EpochNumber;
			EpochPosition = message.EpochPosition;
			EpochId = message.EpochId;
			EpochLeaderInstanceId = message.EpochLeaderInstanceId;
			LastCommitPosition = message.LastCommitPosition;
			WriterCheckpoint = message.WriterCheckpoint;
			ChaserCheckpoint = message.ChaserCheckpoint;

			NodePriority = message.NodePriority;
			ClusterInfo = message.ClusterInfo;
		}
	}


	public class ProposalDto {
		public Guid ServerId { get; set; }
		public Guid LeaderId { get; set; }

		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }
		public string LeaderHttpAddress { get; set; }
		public int LeaderHttpPort { get; set; }

		public int View { get; set; }

		public long LastCommitPosition { get; set; }
		public long WriterCheckpoint { get; set; }
		public long ChaserCheckpoint { get; set; }
		public int EpochNumber { get; set; }
		public long EpochPosition { get; set; }
		public Guid EpochId { get; set; }
		public Guid EpochLeaderInstanceId { get; set; }
		public int NodePriority { get; set; }

		public ProposalDto() {
		}

		public ProposalDto(ElectionMessage.Proposal message) {
			ServerId = message.ServerId;
			LeaderId = message.LeaderId;

			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();
			LeaderHttpAddress = message.LeaderHttpEndPoint.GetHost();
			LeaderHttpPort = message.LeaderHttpEndPoint.GetPort();

			View = message.View;
			EpochNumber = message.EpochNumber;
			EpochPosition = message.EpochPosition;
			EpochId = message.EpochId;
			EpochLeaderInstanceId = message.EpochLeaderInstanceId;
			LastCommitPosition = message.LastCommitPosition;
			WriterCheckpoint = message.WriterCheckpoint;
			ChaserCheckpoint = message.ChaserCheckpoint;
			NodePriority = message.NodePriority;
		}
	}


	public class AcceptDto {
		public Guid ServerId { get; set; }
		public Guid LeaderId { get; set; }

		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }
		public string LeaderHttpAddress { get; set; }
		public int LeaderHttpPort { get; set; }

		public int View { get; set; }

		public AcceptDto() {
		}

		public AcceptDto(ElectionMessage.Accept message) {
			ServerId = message.ServerId;
			LeaderId = message.LeaderId;

			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();
			LeaderHttpAddress = message.LeaderHttpEndPoint.GetHost();
			LeaderHttpPort = message.LeaderHttpEndPoint.GetPort();

			View = message.View;
		}
	}
	
	public class LeaderIsResigningDto {
		public Guid LeaderId { get; set; }
		public string LeaderHttpAddress { get; set; }
		public int LeaderHttpPort { get; set; }
		public LeaderIsResigningDto() {
		}

		public LeaderIsResigningDto(ElectionMessage.LeaderIsResigning message) {
			LeaderId = message.LeaderId;
			LeaderHttpAddress = message.LeaderHttpEndPoint.GetHost();
			LeaderHttpPort = message.LeaderHttpEndPoint.GetPort();
		}
	}
	
	public class LeaderIsResigningOkDto {
		public Guid LeaderId { get; set; }
		public string LeaderHttpAddress { get; set; }
		public int LeaderHttpPort { get; set; }
		public Guid ServerId { get; set; }
		public string ServerHttpAddress { get; set; }
		public int ServerHttpPort { get; set; }
		public LeaderIsResigningOkDto() {
		}

		public LeaderIsResigningOkDto(ElectionMessage.LeaderIsResigningOk message) {
			ServerId = message.ServerId;
			ServerHttpAddress = message.ServerHttpEndPoint.GetHost();
			ServerHttpPort = message.ServerHttpEndPoint.GetPort();
			LeaderId = message.LeaderId;
			LeaderHttpAddress = message.LeaderHttpEndPoint.GetHost();
			LeaderHttpPort = message.LeaderHttpEndPoint.GetPort();
		}
	}
}
