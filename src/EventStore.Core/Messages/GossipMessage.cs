// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class GossipMessage {
	[DerivedMessage(CoreMessage.Gossip)]
	public partial class RetrieveGossipSeedSources : Message {
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GotGossipSeedSources : Message {
		public readonly EndPoint[] GossipSeeds;

		public GotGossipSeedSources(EndPoint[] gossipSeeds) {
			GossipSeeds = gossipSeeds;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class Gossip : Message {
		public readonly int GossipRound;

		public Gossip(int gossipRound) {
			GossipRound = gossipRound;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GossipReceived : Message {
		public readonly IEnvelope Envelope;
		public readonly ClusterInfo ClusterInfo;
		public readonly EndPoint Server;

		public GossipReceived(IEnvelope envelope, ClusterInfo clusterInfo, EndPoint server) {
			Envelope = envelope;
			ClusterInfo = clusterInfo;
			Server = server;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class ReadGossip : Message {
		public readonly IEnvelope Envelope;

		public ReadGossip(IEnvelope envelope) {
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class SendGossip : Message {
		public readonly ClusterInfo ClusterInfo;
		public readonly EndPoint ServerEndPoint;

		public SendGossip(ClusterInfo clusterInfo, EndPoint serverEndPoint) {
			ClusterInfo = clusterInfo;
			ServerEndPoint = serverEndPoint;
		}
	}
	
	[DerivedMessage(CoreMessage.Gossip)]
	public partial class ClientGossip : Message {
		public readonly IEnvelope Envelope;

		public ClientGossip(IEnvelope envelope) {
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class SendClientGossip : Message {
		public readonly ClientClusterInfo ClusterInfo;

		public SendClientGossip(ClientClusterInfo clusterInfo) {
			ClusterInfo = clusterInfo;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GossipUpdated : Message {
		public readonly ClusterInfo ClusterInfo;

		public GossipUpdated(ClusterInfo clusterInfo) {
			ClusterInfo = clusterInfo;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GossipSendFailed : Message {
		public readonly string Reason;
		public readonly EndPoint Recipient;

		public GossipSendFailed(string reason, EndPoint recipient) {
			Reason = reason;
			Recipient = recipient;
		}

		public override string ToString() {
			return String.Format("Reason: {0}, Recipient: {1}", Reason, Recipient);
		}
	}
	
	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GetGossip : Message {
		public GetGossip() { }
	}
	
	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GetGossipFailed : Message {
		public readonly string Reason;
		public readonly EndPoint Recipient;

		public GetGossipFailed(string reason, EndPoint recipient) {
			Reason = reason;
			Recipient = recipient;
		}

		public override string ToString() {
			return String.Format("Reason: {0}, Recipient: {1}", Reason, Recipient);
		}
	}
	
	[DerivedMessage(CoreMessage.Gossip)]
	public partial class GetGossipReceived : Message {
		public readonly ClusterInfo ClusterInfo;
		public readonly EndPoint Server;

		public GetGossipReceived(ClusterInfo clusterInfo, EndPoint server) {
			ClusterInfo = clusterInfo;
			Server = server;
		}
	}

	[DerivedMessage(CoreMessage.Gossip)]
	public partial class UpdateNodePriority : Message {
		public readonly int NodePriority;

		public UpdateNodePriority(int priority) {
			NodePriority = priority;
		}
	}
}
