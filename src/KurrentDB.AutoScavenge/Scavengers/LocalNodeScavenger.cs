// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.POC.IO.Core;
using Serilog;

namespace KurrentDB.AutoScavenge.Scavengers;

/// Performs scavenge operations on the node the plugin runs in, without going through its HTTP endpoint. The node
/// may not listen on its advertised endpoint at all, for instance when it is embedded and serves only a unix socket.
public class LocalNodeScavenger : INodeScavenger {
	private static readonly ILogger Log = Serilog.Log.ForContext<LocalNodeScavenger>();

	// the node does not answer every request (it ignores scavenge requests while initializing, for one), and the
	// process manager handles one command at a time, so a reply that never comes must not hold it up for good.
	// the request is retried on a later gossip, as it is when the HTTP scavenger times out.
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

	private readonly IOperationsClient _operationsClient;
	private readonly IClient _client;

	public LocalNodeScavenger(IOperationsClient operationsClient, IClient client) {
		_operationsClient = operationsClient;
		_client = client;
	}

	public async Task<Guid?> TryStartScavengeAsync(string host, int port, CancellationToken token) {
		Log.Information("Starting node scavenge on this node {Host}:{Port}...", host, port);

		using var timeout = WithTimeout(token);

		try {
			var scavengeId = await _operationsClient.StartScavengeAsync(timeout.Token);
			if (scavengeId is null)
				Log.Information("Node scavenge was not started on this node {Host}:{Port}: a scavenge is already running or the chunks are locked", host, port);
			else
				Log.Information("Started node scavenge on this node {Host}:{Port}. ScavengeId: {ScavengeId}", host, port, scavengeId);
			return scavengeId;

		} catch (Exception ex) {
			Log.Information(ex, "Failed to start scavenge on this node {Host}:{Port}", host, port);
			return null;
		}
	}

	public async Task<ScavengeStatus> TryGetScavengeAsync(string host, int port, Guid scavengeId, CancellationToken token) {
		Log.Verbose("Getting node scavenge status from this node {Host}:{Port}...", host, port);

		using var timeout = WithTimeout(token);

		try {
			var last = await _operationsClient.GetLastScavengeAsync(timeout.Token);
			var result = await ScavengeLog.GetStatusAsync(_client, scavengeId, last.ScavengeId, last.Result, timeout.Token);
			Log.Verbose("Got node scavenge status from this node {Host}:{Port}: {Result}", host, port, result);
			return result;

		} catch (Exception ex) {
			Log.Information(ex, "Failed to get scavenge status on this node {Host}:{Port}", host, port);
			return ScavengeStatus.Unknown;
		}
	}

	public async Task<bool> TryPauseScavengeAsync(string host, int port, Guid? scavengeId, CancellationToken token) {
		Log.Information("Stopping node scavenge {ScavengeId} on this node {Host}:{Port}...", scavengeId, host, port);

		using var timeout = WithTimeout(token);

		try {
			if (await _operationsClient.StopScavengeAsync(scavengeId, timeout.Token))
				Log.Information("Stopped node scavenge on this node {Host}:{Port}", host, port);
			else
				// not found means the scavenge has completed, which is as good as paused here
				Log.Information("Node scavenge {ScavengeId} on this node {Host}:{Port} was already stopped", scavengeId, host, port);
			return true;

		} catch (Exception ex) {
			Log.Information(ex, "Failed to stop scavenge on this node {Host}:{Port}", host, port);
			return false;
		}
	}

	private static CancellationTokenSource WithTimeout(CancellationToken token) {
		var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
		timeout.CancelAfter(RequestTimeout);
		return timeout;
	}
}
