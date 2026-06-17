// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using Serilog;

namespace KurrentDB.Components.Cluster;

public class ClusterOperationsService(IPublisher publisher, IAuthorizationProvider authorizer) {
	static readonly ILogger Log = Serilog.Log.ForContext<ClusterOperationsService>();

	static readonly Operation ShutdownOperation = new(Operations.Node.Shutdown);
	static readonly Operation ResignOperation = new(Operations.Node.Resign);

	public async ValueTask ShutdownAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, ShutdownOperation, "shutdown", ct);
		Log.Warning("Node shutdown requested from the admin UI by {User}.", principal.Identity?.Name ?? "(unknown)");
		publisher.Publish(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true));
	}

	public async ValueTask ResignNodeAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, ResignOperation, "resign", ct);
		Log.Warning("Node resignation requested from the admin UI by {User}.", principal.Identity?.Name ?? "(unknown)");
		publisher.Publish(new ClientMessage.ResignNode());
	}

	// Client gossip read (cluster membership). No per-operation authz: it's an informational read gated by
	// the Cluster page's [Authorize] (matching the original behaviour) — kept here so the component never
	// holds an IPublisher itself.
	public async ValueTask<ClientClusterInfo> GetClusterInfoAsync(CancellationToken ct) {
		var response = await RequestClient.RequestAsync<GossipMessage.ClientGossip, GossipMessage.SendClientGossip>(
			publisher, env => new(env), ct);
		return response.ClusterInfo;
	}

	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, string name, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new OperationsAccessDeniedException(name);
	}
}

public class OperationsAccessDeniedException(string operation)
	: System.Exception($"Access denied for operation: {operation}.");
