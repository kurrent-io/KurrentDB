// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using KurrentDB.Core.Services.Transport.Http.Authentication;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Http.Authentication;

public class ClusterSecretAuthenticationProviderTests {
	private const string Secret = "shared-cluster-secret";

	private static HttpContext ContextWithAuth(string headerValue) {
		var ctx = new DefaultHttpContext();
		if (headerValue is not null)
			ctx.Request.Headers["Authorization"] = headerValue;
		return ctx;
	}

	[Fact]
	public async Task matching_secret_authenticates_as_system() {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var authenticated = sut.Authenticate(ContextWithAuth($"Cluster {Secret}"), out var request);

		Assert.True(authenticated);
		Assert.NotNull(request);
		var (status, principal) = await request.AuthenticateAsync();
		Assert.Equal(HttpAuthenticationRequestStatus.Authenticated, status);
		Assert.Same(SystemAccounts.System, principal);
	}

	[Fact]
	public void missing_authorization_header_returns_false() {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var authenticated = sut.Authenticate(ContextWithAuth(null), out var request);

		Assert.False(authenticated);
		Assert.Null(request);
	}

	[Theory]
	[InlineData("Basic dXNlcjpwYXNz")]
	[InlineData("Bearer some.jwt.token")]
	[InlineData("Token abc")]
	public void wrong_scheme_returns_false(string header) {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var authenticated = sut.Authenticate(ContextWithAuth(header), out var request);

		Assert.False(authenticated);
		Assert.Null(request);
	}

	[Fact]
	public void wrong_secret_returns_false() {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var authenticated = sut.Authenticate(ContextWithAuth("Cluster wrong-secret"), out var request);

		Assert.False(authenticated);
		Assert.Null(request);
	}

	[Fact]
	public void cluster_scheme_with_no_parameter_returns_false() {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var authenticated = sut.Authenticate(ContextWithAuth("Cluster"), out var request);

		Assert.False(authenticated);
		Assert.Null(request);
	}

	[Fact]
	public void multiple_authorization_values_returns_false() {
		var sut = new ClusterSecretAuthenticationProvider(Secret);

		var ctx = new DefaultHttpContext();
		ctx.Request.Headers["Authorization"] = new[] { $"Cluster {Secret}", "Basic dXNlcjpwYXNz" };

		var authenticated = sut.Authenticate(ctx, out var request);

		Assert.False(authenticated);
		Assert.Null(request);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void empty_secret_in_constructor_throws(string secret) {
		Assert.Throws<ArgumentException>(() => new ClusterSecretAuthenticationProvider(secret));
	}

}
