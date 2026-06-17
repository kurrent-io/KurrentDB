// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.Components.Cluster;

// Tracks the cluster's gossip live via a catch-up + live subscription to the in-memory $mem-gossip virtual
// stream, which emits a $GossipUpdated event (the node id plus every member's state and endpoint) on every
// gossip change. From one subscription we derive both this node's role and the current leader's address —
// no separate gossip read needed. Subscribing from the start delivers the current gossip first, then changes.
// The cluster view is process-wide, so a single subscription is shared by every UI circuit (singleton).
// Components read CurrentState/LeaderEndpoint and subscribe to Changed to react to failovers without polling.
public sealed class GossipMonitor : IHostedService, IDisposable {
	static readonly ILogger Log = Serilog.Log.ForContext<GossipMonitor>();

	static readonly JsonSerializerOptions JsonOptions = new() {
		Converters = { new JsonStringEnumConverter() },
	};

	readonly IPublisher _publisher;
	readonly CancellationTokenSource _cts = new();
	// Registered both as a singleton and as an IHostedService (via a factory returning this same instance),
	// so the DI container can dispose it more than once. Guard teardown to stay idempotent.
	volatile bool _disposed;

	// A single immutable snapshot, swapped atomically — lets UI threads read all facts consistently and
	// lock-free while the subscription loop updates them on a background thread.
	volatile Snapshot _snapshot;

	// Signature of the last snapshot we raised Changed for — topology/role/health only, NOT the per-round
	// checkpoint movement, so subscribers don't re-render on every gossip round. Touched only on the loop.
	string _signature;

	public GossipMonitor(IPublisher publisher) {
		_publisher = publisher;
	}

	// This node's role, or null before the first gossip arrives.
	public VNodeState? CurrentState => _snapshot?.Local?.State;
	public bool IsLeader => CurrentState == VNodeState.Leader;
	// The current leader's advertised HTTP endpoint, or null when there's no leader (election, single
	// leaderless node) — used by pages that can only operate on the leader to build a "open on the leader" link.
	public NodeHttpEndpoint? LeaderEndpoint => _snapshot?.Leader;
	// All cluster members, ordered stably by advertised endpoint so a member keeps its position across
	// failovers (only its state/highlight changes). Empty before the first gossip arrives.
	public IReadOnlyList<ClusterNode> Members => _snapshot?.Members ?? [];

	// Raised on the subscription's background thread when the membership, any node's role, or health changes
	// (not on per-round checkpoint movement). Marshal to the UI.
	public event Action Changed;

	public Task StartAsync(CancellationToken cancellationToken) {
		_ = RunAsync(_cts.Token);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		if (!_disposed)
			_cts.Cancel();
		return Task.CompletedTask;
	}

	// Catch-up + live subscription: from the start it first delivers the current $GossipUpdated event (the
	// seed), then every subsequent gossip change. Resubscribes on a transient failure (e.g. the read subsystem
	// isn't up yet during startup, or the subscription drops) until cancelled.
	async Task RunAsync(CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			try {
				// Probe with a one-shot read first. It surfaces "not ready"/"busy"/timeout WITHOUT the live
				// subscription's Error-level logging, so a node that's still starting up (the common case when
				// this hosted service starts) doesn't spam the log. Only once the read succeeds — the read
				// subsystem is up — do we open the (Error-logging) live subscription.
				await using (var probe = new Enumerator.ReadStreamBackwards(
					             _publisher, SystemStreams.GossipStream, StreamRevision.End, maxCount: 1,
					             resolveLinks: false, user: SystemAccounts.System, requiresLeader: false,
					             expiryStrategy: DefaultExpiryStrategy.Instance, compatibility: 1, cancellationToken: ct)) {
					while (await probe.MoveNextAsync()) { }
				}

				await using var sub = new Enumerator.StreamSubscription<string>(
					bus: _publisher,
					expiryStrategy: DefaultExpiryStrategy.Instance,
					streamName: SystemStreams.GossipStream,
					checkpoint: null, // from the start — delivers the current gossip, then live changes
					resolveLinks: false,
					user: SystemAccounts.System,
					requiresLeader: false,
					cancellationToken: ct);

				while (await sub.MoveNextAsync()) {
					if (sub.Current is ReadResponse.EventReceived received)
						Apply(received.Event.Event);
				}
			} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
				return;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				Log.Verbose("Node not ready yet; waiting to subscribe to gossip.");
			} catch (ReadResponseException.NotHandled.ServerBusy) {
				Log.Verbose("Node busy; waiting to subscribe to gossip.");
			} catch (ReadResponseException.Timeout) {
				Log.Verbose("Timed out reading gossip; retrying.");
			} catch (Exception ex) {
				Log.Debug(ex, "Gossip subscription dropped; resubscribing.");
			}

			try {
				await Task.Delay(TimeSpan.FromSeconds(2), ct);
			} catch (OperationCanceledException) {
				return;
			}
		}
	}

	void Apply(EventRecord record) {
		if (record.EventType != GossipListenerService.EventType)
			return;
		try {
			var payload = JsonSerializer.Deserialize<GossipPayload>(record.Data.Span, JsonOptions);

			// Stable order by advertised endpoint so each node keeps its position across failovers — only its
			// state (and which one is highlighted) changes, never the ordering. InstanceId breaks ties.
			var members = (payload.Members ?? [])
				.Select(m => new ClusterNode(
					m.InstanceId, m.State, m.HttpEndPointIp, m.HttpEndPointPort, m.IsAlive,
					IsLocal: m.InstanceId == payload.NodeId))
				.OrderBy(n => n.Host, StringComparer.OrdinalIgnoreCase)
				.ThenBy(n => n.Port)
				.ThenBy(n => n.InstanceId)
				.ToArray();

			var local = members.FirstOrDefault(n => n.IsLocal);
			var leaderNode = members.FirstOrDefault(n => n.State == VNodeState.Leader);
			NodeHttpEndpoint? leader = leaderNode is null ? null : new(leaderNode.Host, leaderNode.Port);

			// Refresh the snapshot every round so live readers (e.g. the cluster page) see current data...
			_snapshot = new Snapshot(members, payload.NodeId, local, leader);

			// ...but only raise Changed when something a subscriber renders actually moved.
			var signature = Signature(members);
			if (signature != _signature) {
				_signature = signature;
				Changed?.Invoke();
			}
		} catch (Exception ex) {
			Log.Debug(ex, "Failed to parse a $GossipUpdated event.");
		}
	}

	// Membership + per-node role + health — the facts the UI shows. Excludes checkpoints/positions, which
	// move every gossip round and would otherwise trigger constant re-renders.
	static string Signature(IReadOnlyList<ClusterNode> members) {
		var sb = new StringBuilder();
		foreach (var n in members)
			sb.Append(n.InstanceId).Append(':').Append((int)n.State).Append(n.IsAlive ? '+' : '-').Append('|');
		return sb.ToString();
	}

	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;
		_cts.Cancel();
		_cts.Dispose();
	}

	// The cluster view we derive from one gossip event: the ordered members, this node's id, the local
	// member (null until our own row appears), and the leader's endpoint (null during an election).
	sealed record Snapshot(IReadOnlyList<ClusterNode> Members, Guid NodeId, ClusterNode Local, NodeHttpEndpoint? Leader);

	// Subset of the $GossipUpdated payload we care about (extra fields are ignored by the deserializer).
	readonly record struct GossipPayload(Guid NodeId, GossipMember[] Members);

	readonly record struct GossipMember(
		Guid InstanceId, VNodeState State, bool IsAlive, string HttpEndPointIp, int HttpEndPointPort);
}

// A node's advertised HTTP endpoint (host/IP and port) as seen by clients.
public readonly record struct NodeHttpEndpoint(string Ip, int Port);

// One cluster member as the UI shows it: its role, advertised client endpoint, liveness, and whether it's
// the node serving this UI.
public sealed record ClusterNode(Guid InstanceId, VNodeState State, string Host, int Port, bool IsAlive, bool IsLocal);
