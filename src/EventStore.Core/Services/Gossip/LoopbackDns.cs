// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Gossip;

public class KnownEndpointGossipSeedSource : IGossipSeedSource {
	private readonly EndPoint[] _ipEndPoints;

	public KnownEndpointGossipSeedSource(EndPoint[] ipEndPoints) {
		_ipEndPoints = Ensure.NotNull(ipEndPoints);
	}

	public IAsyncResult BeginGetHostEndpoints(AsyncCallback requestCallback, object state) {
		requestCallback(null!);
		return null;
	}

	public EndPoint[] EndGetHostEndpoints(IAsyncResult asyncResult) => _ipEndPoints;
}
