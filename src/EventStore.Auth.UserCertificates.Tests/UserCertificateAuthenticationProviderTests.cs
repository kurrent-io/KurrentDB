// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Auth.UserCertificates.Tests;

public class UserCertificateAuthenticationProviderTests {
	private readonly X509Certificate2 _root, _intermediate, _node;
	private readonly UserCertificateAuthenticationProvider _provider;

	public UserCertificateAuthenticationProviderTests() {
		_root = CreateRoot();
		_intermediate = CreateIntermediate(parent: _root);
		_node = CreateNode(parent: _intermediate);
		_provider = new UserCertificateAuthenticationProvider(
			authenticationProvider: new FakeAuthenticationProvider("UserCertificate"),
			getCertificates: () => (
				Node: _node,
				Intermediates: new X509Certificate2Collection(_intermediate),
				Roots: new X509Certificate2Collection(_root)),
			configuration: new ConfigurationBuilder().Build());
	}

	private static X509Certificate2 CreateCertificate(
		bool ca = false,
		X509Certificate2? parent = null,
		bool expired = false,
		bool unready = false,
		string? subject = null,
		bool serverAuthEKU = false,
		bool clientAuthEKU = false) {
		using var rsa = RSA.Create();
		subject ??= TestUtils.GenerateSubject();

		(RSA, X500DistinguishedName)? parentInfo = null;
		if (parent != null) {
			parentInfo = (parent.GetRSAPrivateKey()!, parent.SubjectName);
		}

		return TestUtils.CreateCertificate(ca, rsa, subject, parentInfo, expired, unready,
			serverAuthEKU: serverAuthEKU, clientAuthEKU: clientAuthEKU);
	}

	private static X509Certificate2 CreateRoot() =>
		CreateCertificate(ca: true, parent: null);

	private static X509Certificate2 CreateIntermediate(X509Certificate2 parent) =>
		CreateCertificate(ca: true, parent: parent);

	private static X509Certificate2 CreateNode(X509Certificate2 parent) =>
		CreateCertificate(
			parent: parent,
			serverAuthEKU: true,
			clientAuthEKU: true);

	private static X509Certificate2 CreateClient(X509Certificate2 parent, string userId,
		bool expired = false, bool unready = false) =>

		CreateCertificate(
			parent: parent,
			subject: $"CN={userId}",
			serverAuthEKU: false,
			clientAuthEKU: true,
			expired: expired,
			unready: unready);

	private static X509Certificate2 CreateClientNoEKU(X509Certificate2 parent, string userId) =>
		CreateCertificate(
			parent: parent,
			subject: $"CN={userId}",
			serverAuthEKU: false,
			clientAuthEKU: false);

	[Fact]
	public void with_no_user_certificate() {
		Assert.False(_provider.Authenticate(new DefaultHttpContext(), out _));
	}

	[Fact]
	public void with_proper_user_certificate() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClient(_root, "bob")
			}
		};

		Assert.True(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_user_certificate_having_different_root_ca_than_node() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClient(CreateRoot(), "bob")
			}
		};

		Assert.False(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_user_certificate_having_no_client_auth_eku() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClientNoEKU(_root, "bob")
			}
		};

		Assert.False(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_user_certificate_having_both_server_and_client_auth_eku() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateNode(_root)
			}
		};

		Assert.False(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_expired_user_certificate() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClient(_root, "bob", expired: true)
			}
		};

		Assert.False(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_unready_user_certificate() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClient(_root, "bob", unready: true)
			}
		};

		Assert.False(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_proper_user_certificate_and_cache() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateClient(_root, "bob")
			}
		};
		ctx.Features.Set<IConnectionItemsFeature>(new DefaultConnectionContext());

		Assert.True(_provider.Authenticate(ctx, out _));

		var items = ctx.Features.Get<IConnectionItemsFeature>()?.Items;
		Assert.NotNull(items);
		Assert.True(items.TryGetValue("UserCertificateAuthenticationStatus", out var cachedValue));
		var (authenticated, userId) = Assert.IsType<(bool, string)>(cachedValue);
		Assert.True(authenticated);
		Assert.Equal("bob", userId);
		Assert.True(_provider.Authenticate(ctx, out _));
	}

	[Fact]
	public void with_invalid_user_certificate_and_cache() {
		var ctx = new DefaultHttpContext {
			Connection = {
				ClientCertificate = CreateNode(parent: _root)
			}
		};
		ctx.Features.Set<IConnectionItemsFeature>(new DefaultConnectionContext());

		Assert.False(_provider.Authenticate(ctx, out _));

		var items = ctx.Features.Get<IConnectionItemsFeature>()?.Items;
		Assert.NotNull(items);
		Assert.True(items.TryGetValue("UserCertificateAuthenticationStatus", out var cachedValue));
		var (authenticated, userId) = Assert.IsType<(bool, string)>(cachedValue);
		Assert.False(authenticated);
		Assert.Null(userId);
		Assert.False(_provider.Authenticate(ctx, out _));
	}
}
