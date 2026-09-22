// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane.Raft.StateMachine.Queries;

internal readonly record struct PersistentDatabaseNode(
	EndPoint Address,
	DatabaseNodeRole Role,
	bool IsLeader,
	string Version,
	EndPoint? ClientApi,
	EndPoint Replication,
	Guid InstanceId,
	int ClientTcpApiPort,
	bool ClientTcpApiIsSecure) {
	public DatabaseNode ToEntity(string databaseId) => new() {
		Address = Address,
		DatabaseId = databaseId,
		Role = Role,
		ClientApiAddress = ClientApi,
		ReplicationProtocolAddress = Replication,
		Version = Version,
		InstanceId = InstanceId,
		ClientTcpApiPort = ClientTcpApiPort,
		ClientTcpApiIsSecure = ClientTcpApiIsSecure
	};
}
