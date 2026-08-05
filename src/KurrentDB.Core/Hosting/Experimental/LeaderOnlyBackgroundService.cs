// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.Hosting.Experimental;

/// <summary>
/// Chassis A — "run only while the node holds the role".
///
/// Delegates the lease to an injected <see cref="INodeLifetimeService"/>: acquires when the node takes
/// the role, runs the body under a token that is REVOKED the instant the node loses it, then waits to
/// re-acquire. That is the revoke-on-falling-edge promise, correct for role-exclusive work — a
/// single-writer control plane — that must stop the moment the node is no longer eligible.
///
/// The lease service defines WHICH role (today <c>NodeLifetimeService</c> is leader-only; generalising
/// it to any role set is the parallel next step). This chassis only runs the acquire → run → revoke
/// loop.
/// </summary>
public abstract class LeaderOnlyBackgroundService : SystemBackgroundService {
	protected LeaderOnlyBackgroundService(
        IPublisher publisher,
		INodeLifetimeService nodeLifetime,
		GetNodeSystemInfo getNodeSystemInfo,
		ILogger logger
    ) : base(publisher) {
        NodeLifetime      = nodeLifetime;
        GetNodeSystemInfo = getNodeSystemInfo;
        Logger            = logger;
    }
    
    INodeLifetimeService NodeLifetime      { get; }
	GetNodeSystemInfo    GetNodeSystemInfo { get; }
    ILogger              Logger            { get; }

	protected sealed override async Task RunAsync(CancellationToken stoppingToken) {
		while (!stoppingToken.IsCancellationRequested) {
			// A live token while the node holds the role; an already-cancelled token on shutdown.
			var leaseToken = await NodeLifetime.WaitForLeadershipAsync(stoppingToken);

			if (stoppingToken.IsCancellationRequested)
				break;

			Logger.LogRoleAcquired(ServiceName);

			// The body runs until it loses the role (leaseToken) OR the service stops (stoppingToken).
			using var scope = CancellationTokenSource.CreateLinkedTokenSource(leaseToken, stoppingToken);
			try {
				var nodeInfo = await GetNodeSystemInfo(stoppingToken);
				await OnExecuteAsync(nodeInfo, scope.Token);
			}
			catch (OperationCanceledException) {
				// Role lost or shutting down — the loop condition decides which.
			}
			catch (Exception ex) {
				Logger.LogRoleScopedError(ex, ServiceName, ex.Message);
				break;
			}

			if (!stoppingToken.IsCancellationRequested)
				Logger.LogRoleRevoked(ServiceName);
		}
	}

	/// <summary>
	/// The body, run while the node holds the role. The token is cancelled on loss or on service stop.
	/// It may be invoked again if the role is regained, so it must be safe to re-enter.
	/// </summary>
	protected abstract Task OnExecuteAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}

static partial class LeaderOnlyBackgroundServiceLogMessages {
	[LoggerMessage(LogLevel.Debug, "{ServiceName} acquired its role, running")]
	internal static partial void LogRoleAcquired(this ILogger logger, string serviceName);

	[LoggerMessage(LogLevel.Debug, "{ServiceName} lost its role, stopping until it is regained")]
	internal static partial void LogRoleRevoked(this ILogger logger, string serviceName);

	[LoggerMessage(LogLevel.Error, "{ServiceName} faulted and will not run again: {ErrorMessage}")]
	internal static partial void LogRoleScopedError(this ILogger logger, Exception error, string serviceName, string errorMessage);
}
