// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using Serilog;

namespace EventStore.Core.Services;

public class GrpcSendService :
	IHandle<GrpcMessage.SendOverGrpc> {

	private readonly EventStoreClusterClientCache _eventStoreClientCache;
	private static readonly ILogger Log = Serilog.Log.ForContext<GrpcSendService>();

	public GrpcSendService(EventStoreClusterClientCache eventStoreClientCache) {
		_eventStoreClientCache =
			eventStoreClientCache ?? throw new ArgumentNullException(nameof(eventStoreClientCache));
	}

	public void Handle(GrpcMessage.SendOverGrpc message) {
		if (message.LiveUntil < DateTime.Now) {
			Log.Verbose("Dropping gRPC send message due to TTL being over. {messageType} To : {endPoint}",
				message.Message.GetType().Name, message.DestinationEndpoint);
			return;
		}

		switch (message.Message) {
			case GossipMessage.SendGossip sendGossip:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendGossip(sendGossip, message.DestinationEndpoint, message.LiveUntil);
				break;
			case GossipMessage.GetGossip _:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.GetGossip(message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.ViewChange viewChange:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendViewChange(viewChange, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.ViewChangeProof proof:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendViewChangeProof(proof, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.Prepare prepare:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendPrepare(prepare, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.PrepareOk prepareOk:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendPrepareOk(prepareOk, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.Proposal proposal:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendProposal(proposal, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.Accept accept:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendAccept(accept, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.LeaderIsResigning resigning:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendLeaderIsResigning(resigning, message.DestinationEndpoint, message.LiveUntil);
				break;
			case ElectionMessage.LeaderIsResigningOk resigningOk:
				_eventStoreClientCache.Get(message.DestinationEndpoint)
					.SendLeaderIsResigningOk(resigningOk, message.DestinationEndpoint, message.LiveUntil);
				break;
			default:
				throw new NotImplementedException($"Message of type {message.Message.GetType().Name} cannot be handled.");
		}
	}
}
