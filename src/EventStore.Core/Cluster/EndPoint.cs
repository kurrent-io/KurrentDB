// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Cluster;

public partial class EndPoint {
	public EndPoint(string address, uint port) {
		address_ = address;
		port_ = port;
	}
}
