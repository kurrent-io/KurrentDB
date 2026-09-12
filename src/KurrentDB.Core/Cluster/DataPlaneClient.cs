// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using Grpc.Core;
using KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;
using KurrentDB.Core.Settings;
using KurrentDB.DataPlane.Transport.Grpc;

namespace KurrentDB.Core.Cluster;

class DataPlaneClient(INodeHttpClientFactory httpClientFactory, string uriScheme) : GrpcDataPlaneClient {
	protected override IDisposable CreateChannel(EndPoint address, out CallInvoker invoker) {
		var channel = httpClientFactory.CreateChannel(uriScheme, address);
		invoker = channel.CreateCallInvoker().WithUnaryTimeout(ESConsts.KPlaneUnaryCallTimeoutMs);
		return channel;
	}
}
