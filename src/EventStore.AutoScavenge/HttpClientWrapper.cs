// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;

namespace EventStore.AutoScavenge;

// An HttpClient with additional context
public class HttpClientWrapper {
	public HttpClient HttpClient { get; }
	public string Protocol { get; }

	public HttpClientWrapper(EventStoreOptions options, INodeHttpClientFactory nodeHttpClientFactory) {
		var nodeHttpClient =
			nodeHttpClientFactory.CreateHttpClient(options.DiscoverViaDns ? [options.ClusterDns] : null);

		Protocol = options.Insecure ? "http" : "https";

		HttpClient = nodeHttpClient;
		HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
		HttpClient.Timeout = TimeSpan.FromSeconds(1);
	}
}
