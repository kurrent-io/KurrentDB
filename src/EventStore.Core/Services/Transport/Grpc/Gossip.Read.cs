// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Gossip;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using ClusterInfo = EventStore.Client.Gossip.ClusterInfo;
using MemberInfo = EventStore.Client.Gossip.MemberInfo;

namespace EventStore.Core.Services.Transport.Grpc;

partial class Gossip {
	private static readonly Operation ReadOperation = new(Plugins.Authorization.Operations.Node.Gossip.ClientRead);

	public override async Task<ClusterInfo> Read(Empty request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ReadOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var tcs = new TaskCompletionSource<ClusterInfo>();
		var duration = _tracker.Start();
		_bus.Publish(new GossipMessage.ClientGossip(new CallbackEnvelope(msg => GossipResponse(msg, tcs, duration))));
		return await tcs.Task;
	}

	private void GossipResponse(Message msg, TaskCompletionSource<ClusterInfo> tcs, Duration duration) {
		if (msg is GossipMessage.SendClientGossip received) {
			tcs.TrySetResult(ToGrpcClusterInfo(received.ClusterInfo));
			duration.Dispose();
		}
	}

	private static ClusterInfo ToGrpcClusterInfo(Core.Cluster.ClientClusterInfo cluster) {
		var members = Array.ConvertAll(cluster.Members, x => new MemberInfo {
			InstanceId = Uuid.FromGuid(x.InstanceId).ToDto(),
			TimeStamp = x.TimeStamp.ToTicksSinceEpoch(),
			State = (MemberInfo.Types.VNodeState)x.State,
			IsAlive = x.IsAlive,
			HttpEndPoint = new EndPoint {
				Address = x.HttpEndPointIp,
				Port = (uint)x.HttpEndPointPort
			}
		}).ToArray();
		var info = new ClusterInfo();
		info.Members.AddRange(members);
		return info;
	}
}
