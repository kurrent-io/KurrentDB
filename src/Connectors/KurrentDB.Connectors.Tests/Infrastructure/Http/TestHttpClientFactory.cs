// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Tests.Infrastructure.Http;

public class TestHttpClientFactory(TestHttpMessageHandler testHttpMessageHandler) : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new(testHttpMessageHandler);
}