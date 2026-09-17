// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.Cluster;

// What the rest of the node needs to know about the leader.
// It is a subset of the information stored in <see cref="MemberInfo"/> but arranged around allowing
// the leader to be reached from elsewhere so that KPlane, Gossip, and Elections can all populate it.
public record MemberInfoLite {
	public required Guid InstanceId { get; init; }
	public required EndPoint HttpEndPoint { get; init; }
	public required EndPoint ReplicationEndPoint { get; init; }
	public required EndPoint ClientHttpEndPoint { get; init; }
	public int ClientTcpPort { get; init; }
	public bool ClientTcpApiIsSecure { get; init; }
	public required int EpochNumber { get; init; }

	public bool HasReplicationEndPoint(EndPoint endPoint) =>
		endPoint is not null &&
		ReplicationEndPoint is not null &&
		ReplicationEndPoint.EndPointEquals(endPoint);

	// The TCP API is reached at the client-facing address, on a port of its own - the same way the
	// Kontrol Plane's DatabaseNode models it. Zero means the node does not expose the TCP API.
	[CanBeNull]
	public EndPoint ClientTcpEndPoint =>
		ClientTcpPort is not 0
			? new DnsEndPoint(ClientHttpEndPoint.GetHost(), ClientTcpPort)
			: null;

	// The port a client uses to reach the TCP API, or 0 if the node does not expose one. As with
	// ToClientEndPoint the override may be absent, in which case the endpoint supplies it.
	public static int ToClientTcpPort(EndPoint endPoint, int advertisePort) =>
		endPoint is null
			? 0
			: advertisePort is not 0
				? advertisePort
				: endPoint.GetPort();

	// Applies the advertise-to-client overrides to an endpoint, as LeaderInfoProvider does for this
	// node's own addresses. Either override may be absent, in which case the endpoint supplies it.
	public static EndPoint ToClientEndPoint(EndPoint endPoint, string advertiseHost, int advertisePort) =>
		endPoint is null
			? null
			: new DnsEndPoint(
				host: string.IsNullOrEmpty(advertiseHost) ? endPoint.GetHost() : advertiseHost,
				port: advertisePort is 0 ? endPoint.GetPort() : advertisePort);
}
