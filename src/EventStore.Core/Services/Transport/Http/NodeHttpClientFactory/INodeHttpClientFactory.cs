// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;

namespace EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;

public interface INodeHttpClientFactory {
	HttpClient CreateHttpClient(string[] additionalCertificateNames);
}
