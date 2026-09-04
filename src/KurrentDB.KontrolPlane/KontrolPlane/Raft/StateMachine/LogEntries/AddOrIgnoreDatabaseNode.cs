// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.Raft.StateMachine.LogEntries;

partial class AddOrIgnoreDatabaseNode : ILogEntry<AddOrIgnoreDatabaseNode>, IDatabaseModificationCommand {
	public const int TypeId = 5;

	static int ILogEntry.TypeId => TypeId;

	public AddOrIgnoreDatabaseNode(DatabaseNode node) {
		Address = node.Address.ToByteString();
		DatabaseId = node.DatabaseId;
		Role = (int)node.Role;
		ReplicationProtocolAddress = node.ReplicationProtocolAddress.ToByteString();
		ClientApiAddress = node.ClientApiAddress.ToByteString();
		Version = node.Version;
		InstanceId = ByteString.CopyFrom(node.InstanceId.ToByteArray());
		ClientTcpApiPort = node.ClientTcpApiPort;
		ClientTcpApiIsSecure = node.ClientTcpApiIsSecure;
	}
}
