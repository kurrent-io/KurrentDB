// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using DotNext.Threading;

namespace KurrentDB.KontrolPlane;

using StateMachine;

/// <summary>
/// Represents Raft-based implementation of <see cref="IKontroller"/> interface.
/// </summary>
public partial class RaftKontroller : IAsyncDisposable {
	private readonly WriteAheadLog _wal;
	private readonly ClusterStateMachine _state;
	private readonly RaftCluster _raft;
	private readonly CancellationTokenMultiplexer _multiplexer;
	private readonly TimeSpan _appointmentExpiration;
	private Task _leadershipTask;

	public RaftKontroller(in Options options) {
		var stateLocation = new DirectoryInfo(Path.Combine(options.WalOptions.Location, "db"));
		var configStorageLocation = Path.Combine(options.WalOptions.Location, "members.list");
		_state = new(stateLocation, options.ConnectionPoolCapacity);
		_wal = new WriteAheadLog(options.WalOptions, _state);

		var config = new RaftCluster.TcpConfiguration(options.ListenAddress) {
			PublicEndPoint = options.PublicAddress,
			ConfigurationStorage = new PersistentConfigurationStorage(configStorageLocation),
		};

		_raft = new RaftCluster(config) {
			AuditTrail = _wal
		};

		_leadershipTask = Task.CompletedTask;
		_multiplexer = new() { MaximumRetained = 128 };
		_appointmentExpiration = options.AppointmentExpiration;
		_appointmentState = new();
	}

	public required IDatabaseReplicaSet ReplicaSet {
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	public async Task StartAsync(CancellationToken token) {
		await _raft.StartAsync(token);
		_leadershipTask = HandleLeadershipAsync();
	}

	public async Task StopAsync(CancellationToken token) {
		await _raft.StopAsync(token);
		await _leadershipTask.ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync() {
		await _raft.DisposeAsync();
		await _wal.DisposeAsync();
		_state.Dispose();
	}
}
