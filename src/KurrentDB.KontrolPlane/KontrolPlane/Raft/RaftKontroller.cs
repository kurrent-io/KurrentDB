// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KurrentDB.KontrolPlane.Raft;

using DataPlane;
using StateMachine;

/// <summary>
/// Represents Raft-based implementation of <see cref="IKontroller"/> interface.
/// </summary>
public partial class RaftKontroller : IAsyncDisposable {
	private readonly WriteAheadLog _wal;
	private readonly ClusterStateMachine _state;
	private readonly RaftCluster _raft;
	private readonly IReadOnlySet<EndPoint> _seed;
	private readonly IClusterConfigurationStorage<EndPoint> _raftMembershipStorage;
	private Task _leadershipTask;

	public RaftKontroller(in Options options) {
		var stateLocation = new DirectoryInfo(Path.Combine(options.PersistentStateRoot, "replicated_state"));
		var configStorageLocation = Path.Combine(options.PersistentStateRoot, "members.list");
		_state = new(stateLocation, options.ConnectionPoolCapacity) {
			SnapshotDepth = options.SnapshotDepth
		};

		// Must be recovered before initialization of the WAL, which can apply log entries at construction time
		_state.Recover();
		_wal = new(options.WalOptions, _state);

		_seed = options.Nodes;
		_raftMembershipStorage = new PersistentConfigurationStorage(configStorageLocation);
		var config = new RaftCluster.TcpConfiguration(options.ListenAddress) {
			PublicEndPoint = options.PublicAddress,
			ConfigurationStorage = _raftMembershipStorage,
			ColdStart = _seed.Count is 0,
			LowerElectionTimeout = options.LowerElectionTimeout,
			UpperElectionTimeout = options.UpperElectionTimeout,
		};

		_raft = new(config) {
			AuditTrail = _wal
		};

		// When Raft node is added or removed, we want to notify database nodes about new set of Kontrol Plane nodes
		Action<RaftCluster<RaftClusterMember>, RaftClusterMemberEventArgs<RaftClusterMember>> notifier = _state.NotifyAllTrackers;
		_raft.MemberAdded += notifier;
		_raft.MemberRemoved += notifier;

		_leadershipTask = Task.CompletedTask;
		_appointmentDuration = options.AppointmentDuration;
		_appointmentState = new();
		_lifecycleTokenSource = new();
		_lifecycleToken = _lifecycleTokenSource.Token;
	}

	public required Func<IDataPlane> DataPlaneClientFactory {
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	public async Task StartAsync(CancellationToken token) {
		await PopulateRaftClusterNodesAsync(_raftMembershipStorage, _seed, token);
		await _raft.StartAsync(token);
		_leadershipTask = HandleLeadershipAsync();
	}

	private static async Task PopulateRaftClusterNodesAsync(
		IClusterConfigurationStorage<EndPoint> storage,
		IReadOnlySet<EndPoint> nodes,
		CancellationToken token) {
		var configuration = await storage.LoadConfigurationAsync(token);

		// If persistent storage contains a list of Raft nodes, do not load Seed
		if (configuration.Members.Count is 0) {
			configuration = nodes
				.Aggregate(configuration, static (current, raftNodeAddress) => current.Add(raftNodeAddress));

			await storage.SaveConfigurationAsync(configuration, configurationVersion: 0L, token);
		}
	}

	public async Task StopAsync(CancellationToken token) {
		await _raft.StopAsync(token);
		await CancelAsync();
		await _leadershipTask.ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync() {
		await CancelAsync();
		await _raft.DisposeAsync();
		await _wal.DisposeAsync();
		await _state.DisposeAsync();
	}

	private ValueTask CancelAsync() {
		return Interlocked.Exchange(ref _lifecycleTokenSource, null) is { } cts
			? CancelAndDiposeAsync(cts)
			: ValueTask.CompletedTask;

		static async ValueTask CancelAndDiposeAsync(CancellationTokenSource cts) {
			using (cts) {
				await cts.CancelAsync();
			}
		}
	}
}

file static class ClusterStateMachineExtensions {
	public static void NotifyAllTrackers(this ClusterStateMachine state,
		RaftCluster<RaftClusterMember> cluster,
		RaftClusterMemberEventArgs<RaftClusterMember> args)
		=> state.NotifyAllTrackers();
}
