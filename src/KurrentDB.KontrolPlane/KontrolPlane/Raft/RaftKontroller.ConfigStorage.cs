// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Buffers;
using DotNext.Net;
using DotNext.Net.Cluster.Consensus.Raft.Membership;

namespace KurrentDB.KontrolPlane.Raft;

partial class RaftKontroller {
	private sealed class PersistentConfigurationStorage(string fileName) : PersistentClusterConfigurationStorage<EndPoint>(fileName) {
		protected override void Encode(EndPoint address, ref BufferWriterSlim<byte> writer)
			=> writer.WriteEndPoint(address);

		protected override EndPoint Decode(ref SequenceReader reader) => reader.ReadEndPoint();
	}
}
