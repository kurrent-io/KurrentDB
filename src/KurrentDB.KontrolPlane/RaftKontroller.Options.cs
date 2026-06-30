// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KurrentDB.KontrolPlane;

partial class RaftKontroller {
	/// <summary>
	/// Represents Kontroller options.
	/// </summary>
	public readonly struct Options {
		public required WriteAheadLog.Options WalOptions {
			get;
			init;
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

		public required TimeSpan AppointmentExpiration {
			get;
			init => field = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}

		public bool SingleNodeDeployment {
			get;
			init;
		}

		public int SnapshotDepth {
			get => field is 0 ? 100 : field;
			init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
		}
	}
}
