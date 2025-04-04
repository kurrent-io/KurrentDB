// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.Cluster;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using ClusterInfo = EventStore.Core.Cluster.ClusterInfo;
using Empty = EventStore.Client.Empty;

namespace EventStore.Core.Services.Transport.Grpc.Cluster;

partial class Elections {
	private static readonly Empty EmptyResult = new Empty();
	private readonly IPublisher _bus;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly string _clusterDns;
	private static readonly Operation ViewChangeOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.ViewChange);
	private static readonly Operation ViewChangeProofOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.ViewChangeProof);
	private static readonly Operation PrepareOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.Prepare);
	private static readonly Operation PrepareOkOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.PrepareOk);
	private static readonly Operation ProposalOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.Proposal);
	private static readonly Operation AcceptOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.Accept);
	private static readonly Operation MasterIsResigningOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.LeaderIsResigning);
	private static readonly Operation MasterIsResigningOkOperation = new Operation(Plugins.Authorization.Operations.Node.Elections.LeaderIsResigningOk);


	public Elections(IPublisher bus, IAuthorizationProvider authorizationProvider, string clusterDns) {
		_bus = bus;
		_authorizationProvider = authorizationProvider ?? throw new ArgumentNullException(nameof(authorizationProvider));
		_clusterDns = clusterDns;
	}

	public override async Task<Empty> ViewChange(ViewChangeRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ViewChangeOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.ViewChange(
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address, (int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			request.AttemptedView));
		return EmptyResult;
	}

	public override async Task<Empty> ViewChangeProof(ViewChangeProofRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ViewChangeProofOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.ViewChangeProof(
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address, (int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			request.InstalledView));
		return EmptyResult;
	}

	public override async Task<Empty> Prepare(PrepareRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, PrepareOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.Prepare(
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address, (int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			request.View));
		return EmptyResult;
	}

	public override async Task<Empty> PrepareOk(PrepareOkRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, PrepareOkOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.PrepareOk(
			request.View,
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address, (int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			request.EpochNumber,
			request.EpochPosition,
			Uuid.FromDto(request.EpochId).ToGuid(),
			Uuid.FromDto(request.EpochLeaderInstanceId).ToGuid(),
			request.LastCommitPosition,
			request.WriterCheckpoint,
			request.ChaserCheckpoint,
			request.NodePriority,
			ClusterInfo.FromGrpcClusterInfo(request.ClusterInfo, _clusterDns)));
		return EmptyResult;
	}

	public override async Task<Empty> Proposal(ProposalRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ProposalOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.Proposal(
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address, (int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			Uuid.FromDto(request.LeaderId).ToGuid(),
			new DnsEndPoint(request.LeaderHttp.Address, (int)request.LeaderHttp.Port).WithClusterDns(_clusterDns),
			request.View,
			request.EpochNumber,
			request.EpochPosition,
			Uuid.FromDto(request.EpochId).ToGuid(),
			Uuid.FromDto(request.EpochLeaderInstanceId).ToGuid(),
			request.LastCommitPosition,
			request.WriterCheckpoint,
			request.ChaserCheckpoint,
			request.NodePriority));
		return EmptyResult;
	}

	public override async Task<Empty> Accept(AcceptRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, AcceptOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.Accept(
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address,
				(int)request.ServerHttp.Port).WithClusterDns(_clusterDns),
			Uuid.FromDto(request.LeaderId).ToGuid(),
			new DnsEndPoint(request.LeaderHttp.Address,
				(int)request.LeaderHttp.Port).WithClusterDns(_clusterDns),
			request.View));
		return EmptyResult;
	}

	public override async Task<Empty> LeaderIsResigning(LeaderIsResigningRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, MasterIsResigningOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.LeaderIsResigning(
			Uuid.FromDto(request.LeaderId).ToGuid(),
			new DnsEndPoint(request.LeaderHttp.Address,
				(int)request.LeaderHttp.Port).WithClusterDns(_clusterDns)));
		return EmptyResult;
	}

	public override async Task<Empty> LeaderIsResigningOk(LeaderIsResigningOkRequest request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, MasterIsResigningOkOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		_bus.Publish(new ElectionMessage.LeaderIsResigningOk(
			Uuid.FromDto(request.LeaderId).ToGuid(),
			new DnsEndPoint(request.LeaderHttp.Address,
				(int)request.LeaderHttp.Port).WithClusterDns(_clusterDns),
			Uuid.FromDto(request.ServerId).ToGuid(),
			new DnsEndPoint(request.ServerHttp.Address,
				(int)request.ServerHttp.Port).WithClusterDns(_clusterDns)));
		return EmptyResult;
	}
}
