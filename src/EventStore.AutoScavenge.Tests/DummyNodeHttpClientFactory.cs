// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;

namespace EventStore.AutoScavenge.Tests;

public class DummyNodeHttpClientFactory : INodeHttpClientFactory {
	public HttpClient CreateHttpClient(string[] additionalCertificateNames) => new();
}
