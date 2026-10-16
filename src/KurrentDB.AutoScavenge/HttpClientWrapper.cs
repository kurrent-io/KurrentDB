// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;

namespace KurrentDB.AutoScavenge;

// An HttpClient with additional context
public class HttpClientWrapper {
	public HttpClient HttpClient { get; }
	public string Protocol { get; }

	public HttpClientWrapper(EventStoreOptions options, INodeHttpClientFactory nodeHttpClientFactory)
		: this(
			nodeHttpClientFactory.CreateHttpClient(options.DiscoverViaDns ? [options.ClusterDns] : null),
			protocol: options.TlsDisabled() ? "http" : "https") {
	}

	private HttpClientWrapper(HttpClient httpClient, string protocol) {
		HttpClient = httpClient;
		Protocol = protocol;

		HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
		HttpClient.Timeout = TimeSpan.FromSeconds(1);
	}

	/// <summary>
	/// A client for this node only, connecting the way its host provides rather than to the endpoint the node
	/// advertises. That connection is cleartext whatever the node's TLS settings.
	/// </summary>
	public static HttpClientWrapper ForLocalNode(ILocalNodeHttpHandlerFactory localNodeHttpHandlerFactory) =>
		new(new HttpClient(localNodeHttpHandlerFactory.CreateHandler()), protocol: "http");
}
