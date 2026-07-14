// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Describes database node.
/// </summary>
public sealed record DatabaseNode : IEntity {
	internal readonly EndPoint? ClientApiAddressInternal;

	public required string DatabaseId { get; init; }

	/// <summary>
	/// The address of the database node.
	/// </summary>
	/// <remarks>
	/// The endpoint contains an address + port, which is used to access V2 and internal gRPC API.
	/// </remarks>
	public required EndPoint Address { get; init; }

	public DatabaseNodeRole Role { get; init; }

	/// <summary>
	/// The address visible to the database node clients.
	/// </summary>
	[AllowNull]
	public EndPoint ClientApiAddress {
		get => ClientApiAddressInternal ?? Address;
		init => ClientApiAddressInternal = value;
	}

	/// <summary>
	/// The address of the replication endpoint.
	/// </summary>
	public required EndPoint ReplicationProtocolAddress {
		get;
		init;
	}

	/// <summary>
	/// Gets or sets running KDB version on the node.
	/// </summary>
	public string Version {
		get;
		init;
	} = string.Empty;

	public Guid InstanceId {
		get;
		init;
	}
}
