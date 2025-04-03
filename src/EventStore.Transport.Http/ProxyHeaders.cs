// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Transport.Http;

public static class ProxyHeaders {
	public const string XForwardedPort = "X-Forwarded-Port";
	public const string XForwardedProto = "X-Forwarded-Proto";
	public const string XForwardedHost = "X-Forwarded-Host";
	public const string XForwardedPrefix = "X-Forwarded-Prefix";
}

public static class ProxyHeaderValues {
	public const string XForwardedProtoHttp = "http";
	public const string XForwardedProtoHttps = "https";
}
