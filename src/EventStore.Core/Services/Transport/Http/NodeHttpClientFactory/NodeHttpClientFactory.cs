// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Settings;
using Serilog;

namespace EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;

public class NodeHttpClientFactory(
	string uriScheme,
	CertificateDelegates.ServerCertificateValidator nodeCertificateValidator,
	Func<X509Certificate> clientCertificateSelector) : INodeHttpClientFactory {

	public HttpClient CreateHttpClient(string[] additionalCertificateNames) {
		HttpMessageHandler httpMessageHandler;
		if (uriScheme == Uri.UriSchemeHttps){
			var socketsHttpHandler = new SocketsHttpHandler {
				SslOptions = {
					CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
					RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
						var (isValid, error) = nodeCertificateValidator(certificate, chain, errors, additionalCertificateNames);
						if (!isValid && error != null) {
							Log.Error("Server certificate validation error: {e}", error);
						}

						return isValid;
					},
					LocalCertificateSelectionCallback = delegate {
						return clientCertificateSelector();
					}
				},
				PooledConnectionLifetime = ESConsts.HttpClientConnectionLifeTime
			};

			httpMessageHandler = socketsHttpHandler;
		} else {
			httpMessageHandler = new SocketsHttpHandler();
		}

		return new HttpClient(httpMessageHandler);
	}
}
