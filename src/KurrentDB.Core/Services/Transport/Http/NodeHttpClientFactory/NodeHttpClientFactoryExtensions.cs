// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading;
using Grpc.Net.Client;
using KurrentDB.Common.Utils;
using Serilog.Extensions.Logging;

namespace KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;

public static class NodeHttpClientFactoryExtensions {
	public static GrpcChannel CreateChannel(
		this INodeHttpClientFactory httpClientFactory,
		string uriScheme,
		EndPoint address) {

		var httpClient = httpClientFactory.CreateHttpClient(address.GetOtherNames());
		// infinite so that streaming calls do not timeout. see also CallInvokerWithUnaryCallTimeout
		httpClient.Timeout = Timeout.InfiniteTimeSpan;
		httpClient.DefaultRequestVersion = new Version(2, 0);

		return GrpcChannel.ForAddress(
			new UriBuilder(uriScheme, address.GetHost(), address.GetPort()).Uri,
			new GrpcChannelOptions {
				HttpClient = httpClient,
				DisposeHttpClient = true,
				LoggerFactory = new SerilogLoggerFactory(),
			});
	}
}
