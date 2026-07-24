// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

partial class DatabaseNode {
	public DatabaseNode(KontrolPlane.DatabaseNode node) {
		DatabaseId = node.DatabaseId;
		InstanceId = ByteString.CopyFrom(node.InstanceId.ToByteArray());
		ClientApiAddress = node.ClientApiAddress.ToByteString();
		Version = node.Version;
		ReplicationAddress = node.ReplicationProtocolAddress.ToByteString();
		Role = Convert(node.Role);
		NodeAddress = node.Address.ToByteString();
	}

	public KontrolPlane.DatabaseNode ToEntity() => new() {
		DatabaseId = DatabaseId,
		Address = NodeAddress.ToEndPoint(),
		ReplicationProtocolAddress = ReplicationAddress.ToEndPoint(),
		ClientApiAddress = ClientApiAddress.ToEndPoint(),
		InstanceId = new(InstanceId.Span),
		Role = Convert(Role),
		Version = Version,
	};

	private static KontrolPlane.DatabaseNodeRole Convert(DatabaseNodeRole role) => role switch {
		DatabaseNodeRole.DbNodeRegular => KontrolPlane.DatabaseNodeRole.Regular,
		DatabaseNodeRole.DbNodeReadonlyReplica => KontrolPlane.DatabaseNodeRole.ReadOnlyReplica,
		_ => throw new ArgumentOutOfRangeException(nameof(role)),
	};

	private static DatabaseNodeRole Convert(KontrolPlane.DatabaseNodeRole role) => role switch {
		KontrolPlane.DatabaseNodeRole.Regular => DatabaseNodeRole.DbNodeRegular,
		KontrolPlane.DatabaseNodeRole.ReadOnlyReplica => DatabaseNodeRole.DbNodeReadonlyReplica,
		_ => throw new ArgumentOutOfRangeException(nameof(role)),
	};
}
