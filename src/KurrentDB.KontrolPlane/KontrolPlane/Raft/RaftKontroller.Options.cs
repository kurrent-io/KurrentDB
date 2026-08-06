// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Collections.Generic;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KurrentDB.KontrolPlane.Raft;

partial class RaftKontroller {
	/// <summary>
	/// Represents Kontroller options.
	/// </summary>
	public readonly struct Options {
		private static readonly int WalChunkSize = Environment.SystemPageSize * 10;
		private const WriteAheadLog.MemoryManagementStrategy WalMemoryManagementStrategy = WriteAheadLog.MemoryManagementStrategy.SharedMemory;
		private const WriteAheadLog.IntegrityHashAlgorithm WalHashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.None;

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
	}
}
