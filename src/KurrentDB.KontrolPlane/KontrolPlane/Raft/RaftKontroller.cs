// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Net.Cluster.Consensus.Raft;
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
	private Task _leadershipTask;

	public RaftKontroller(in Options options) {
		var stateLocation = new DirectoryInfo(Path.Combine(options.WalOptions.Location, "replicated_state"));
		var configStorageLocation = Path.Combine(options.WalOptions.Location, "members.list");
		_state = new(stateLocation, options.ConnectionPoolCapacity) {
			SnapshotDepth = options.SnapshotDepth
		};

		// Must be recovered before initialization of the WAL, which can apply log entries at construction time
		_state.Recover();
		_wal = new(options.WalOptions, _state);

		var config = new RaftCluster.TcpConfiguration(options.ListenAddress) {
			PublicEndPoint = options.PublicAddress,
			ConfigurationStorage = new PersistentConfigurationStorage(configStorageLocation),
			ColdStart = options.SingleNodeDeployment,
		};

		_raft = new(config) {
			AuditTrail = _wal
		};

		_leadershipTask = Task.CompletedTask;
		AppointmentDuration = options.AppointmentDuration;
		_appointmentState = new();
		_lifecycleTokenSource = new();
		_lifecycleToken = _lifecycleTokenSource.Token;
	}

	public required Func<IDataPlane> DataPlaneClientFactory {
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	public async Task StartAsync(CancellationToken token) {
		await _raft.StartAsync(token);
		_leadershipTask = HandleLeadershipAsync();
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
