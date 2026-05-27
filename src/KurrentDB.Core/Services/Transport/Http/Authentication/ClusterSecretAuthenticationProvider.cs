// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http.Headers;
using EventStore.Plugins.Authentication;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Core.Services.Transport.Http.Authentication;

// Authenticates inter-node HTTP requests when TLS is disabled.
// Note that since TLS is disabled the secret will be sent in clear text
public class ClusterSecretAuthenticationProvider : IHttpAuthenticationProvider {
	public string Name => "cluster-secret";

	private readonly string _expectedClusterSecret;

	public ClusterSecretAuthenticationProvider(string expectedClusterSecret) {
		if (string.IsNullOrWhiteSpace(expectedClusterSecret))
			throw new ArgumentException("Node secret must be non-empty.", nameof(expectedClusterSecret));
		_expectedClusterSecret = expectedClusterSecret;
	}

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		request = null!;

		if (!context.Request.Headers.TryGetValue("authorization", out var values) || values.Count != 1)
			return false;

		if (!AuthenticationHeaderValue.TryParse(values[0], out var header) ||
		    header.Scheme != "Cluster" ||
		    header.Parameter is null)
			return false;

		if (!string.Equals(header.Parameter, _expectedClusterSecret, StringComparison.Ordinal))
			return false;

		request = new(context, "system", "");
		request.Authenticated(SystemAccounts.System);
		return true;
	}
}
