// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Collections.Generic;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KurrentDB.KontrolPlane.Raft;

partial class RaftKontroller {
	/// <summary>
	/// Represents Kontroller options.
	/// </summary>
	public readonly struct Options() {
		private static readonly int WalChunkSize = 1024 * 1024; // 1MB
		private const WriteAheadLog.MemoryManagementStrategy WalMemoryManagementStrategy = WriteAheadLog.MemoryManagementStrategy.PrivateMemory;
		private const WriteAheadLog.IntegrityHashAlgorithm WalHashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.None;

		private readonly ElectionTimeout _electionSettings = ElectionTimeout.Recommended;

		internal WriteAheadLog.Options WalOptions => new() {
			Location = PersistentStateRoot,
			HashAlgorithm = WalHashAlgorithm,
			ChunkSize = WalChunkSize,
			MemoryManagement = WalMemoryManagementStrategy
		};

		public required string PersistentStateRoot {
			get;
			init => field = value.Length > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public required IPEndPoint ListenAddress {
			get;
			init;
		}

		public EndPoint PublicAddress {
			get => field ?? ListenAddress;
			init;
		}

		public int ConnectionPoolCapacity {
			get => field > 0 ? field : 10;
			init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public required TimeSpan AppointmentDuration {
			get;
			init => field = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public IReadOnlySet<EndPoint> Nodes {
			get => field ?? IReadOnlySet<EndPoint>.Empty;
			init;
		}

		public int SnapshotDepth {
			get => field is 0 ? 100 : field;
			init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		/// <summary>
		/// Gets lower bound of the Raft election timeout, in milliseconds.
		/// </summary>
		public int LowerElectionTimeout {
			get => _electionSettings.LowerValue;
			init => _electionSettings = _electionSettings with { LowerValue = value };
		}

		/// <summary>
		/// Gets upper bound of the Raft election timeout, in milliseconds.
		/// </summary>
		public int UpperElectionTimeout {
			get => _electionSettings.UpperValue;
			init => _electionSettings = _electionSettings with { UpperValue = value };
		}
	}
}
