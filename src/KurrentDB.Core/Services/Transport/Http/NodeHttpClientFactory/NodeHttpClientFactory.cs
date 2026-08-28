// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Settings;

namespace KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;

public class NodeHttpClientFactory(
	string uriScheme,
	CertificateDelegates.ServerCertificateValidator nodeCertificateValidator,
	Func<X509Certificate> clientCertificateSelector,
	string clusterSecret) : INodeHttpClientFactory {

	public HttpClient CreateHttpClient(string[] additionalCertificateNames) {
		HttpMessageHandler httpMessageHandler;
		if (uriScheme == Uri.UriSchemeHttps) {
			var socketsHttpHandler = new SocketsHttpHandler {
				SslOptions = NodeSslOptions.CreateClientOptions(
					serverCertificateValidator: nodeCertificateValidator,
					clientCertificateSelector: clientCertificateSelector,
					additionalCertificateNames: additionalCertificateNames,
					enabledSslProtocols: NodeTlsPolicy.SystemSslProtocols),
				PooledConnectionLifetime = ESConsts.HttpClientConnectionLifeTime,
			};

			httpMessageHandler = socketsHttpHandler;
		} else {
			httpMessageHandler = new SocketsHttpHandler();
		}

		var client = new HttpClient(httpMessageHandler);
		if (uriScheme != Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(clusterSecret)) {
			// In cleartext (--disable-tls) the node cannot present a client certificate.
			// Carry the shared cluster secret instead so peers can authenticate us as system.
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Cluster", clusterSecret);
		}
		return client;
	}
}
