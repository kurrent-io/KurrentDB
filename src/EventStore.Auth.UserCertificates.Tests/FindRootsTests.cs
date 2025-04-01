// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

using static EventStore.Auth.UserCertificates.Tests.TestUtils;

namespace EventStore.Auth.UserCertificates.Tests;

public class FindRootsTests {
	private static string[] FindRoots(X509Certificate2 leaf, X509Certificate2[]? intermediates, X509Certificate2[]? roots) {
		var intermediatesCollection = intermediates is null ? null : new X509Certificate2Collection(intermediates);
		var rootsCollection = roots is null ? null : new X509Certificate2Collection(roots);
		var rootCAs = CertificateUtils
			.FindRoots(
				leaf,
				intermediatesCollection,
				rootsCollection)
			.Select(x => x.Thumbprint)
			.Order()
			.ToArray();

		// verify that the collections haven't changed
		if (intermediates != null)
			Assert.Equal(new X509Certificate2Collection(intermediates), intermediatesCollection);

		if (roots != null)
			Assert.Equal(new X509Certificate2Collection(roots), rootsCollection);

		return rootCAs;
	}

	[Fact]
	public void with_untrusted_self_signed() {
		var someRoot = CreateCertificate(ca: true);
		var leaf = CreateCertificate(ca: false, parent: null);
		Assert.Empty(
			FindRoots(leaf, null, [someRoot]));
	}

	[Fact]
	public void with_trusted_self_signed() {
		var someRoot = CreateCertificate(ca: true);
		var leaf = CreateCertificate(ca: false, parent: null);
		Assert.Equal(
			[leaf.Thumbprint],
			FindRoots(leaf, null, [leaf, someRoot]));
	}

	[Fact]
	public void with_root() {
		var root = CreateCertificate(ca: true);
		var leaf = CreateCertificate(ca: false, parent: root);
		Assert.Equal(
			[root.Thumbprint],
			FindRoots(leaf, null, [root]));
	}

	[Fact]
	public void with_root_having_same_issuer_name_but_different_keypair() {
		var root = CreateCertificate(ca: true, subject: "CN=root");
		var differentRoot = CreateCertificate(ca: true, subject: "CN=root");
		var leaf = CreateCertificate(ca: false, parent: root);
		Assert.Equal(
			[root.Thumbprint],
			FindRoots(leaf, null, [root, differentRoot]));
	}

	[Fact]
	public void with_expired_root() {
		var root = CreateCertificate(ca: true, expired: true);
		var leaf = CreateCertificate(ca: false, parent: root);
		Assert.Empty(
			FindRoots(leaf, null, [root]));
	}

	[Fact]
	public void with_intermediate() {
		var root = CreateCertificate(ca: true);
		var intermediate = CreateCertificate(ca: true, parent: root);
		var leaf = CreateCertificate(ca: false, parent: intermediate);
		Assert.Equal(
			[root.Thumbprint],
			FindRoots(leaf, [intermediate], [root]));
	}

	[Fact]
	public void with_expired_intermediate() {
		var root = CreateCertificate(ca: true);
		var intermediate = CreateCertificate(ca: true, parent: root, expired: true);
		var leaf = CreateCertificate(ca: false, parent: intermediate);
		Assert.Empty(
			FindRoots(leaf, [intermediate], [root]));
	}

	[Fact]
	public void with_expired_leaf() {
		var root = CreateCertificate(ca: true);
		var intermediate = CreateCertificate(ca: true, parent: root);
		var leaf = CreateCertificate(ca: false, parent: intermediate, expired: true);
		Assert.Empty(
			FindRoots(leaf, [intermediate], [root]));
	}

	[Fact]
	public void with_two_equivalent_roots() {
		using var rsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rsa);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rsa);
		var leaf = CreateCertificate(ca: false, parent: root1);

		Assert.Equal(
			new[] { root1.Thumbprint, root2.Thumbprint }.Order(),
			FindRoots(leaf, null, [root1, root2]));
	}

	[Fact]
	public void with_two_equivalent_roots_and_intermediates() {
		using var rootRsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa);

		using var intermediateRsa = RSA.Create();
		var intermediate1 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate",
			keyPair: intermediateRsa);
		var intermediate2 = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate",
			keyPair: intermediateRsa);

		var leaf = CreateCertificate(ca: false, parent: intermediate1);

		Assert.Equal(
			new[] { root1.Thumbprint, root2.Thumbprint }.Order(),
			FindRoots(leaf, [intermediate1, intermediate2], [root1, root2]));
	}

	// https://github.com/dotnet/runtime/issues/98921
	[Fact(Skip = "failing on linux due to the above issue")]
	public void with_two_different_roots_but_equivalent_intermediates() {
		var root1 = CreateCertificate(ca: true, subject: "CN=root1");
		var root2 = CreateCertificate(ca: true, subject: "CN=root2");

		using var intermediateRsa = RSA.Create();
		var intermediate1 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate",
			keyPair: intermediateRsa);
		var intermediate2 = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate",
			keyPair: intermediateRsa);

		var leaf = CreateCertificate(ca: false, parent: intermediate1);

		Assert.Equal(
			new[] { root1.Thumbprint, root2.Thumbprint }.Order(),
			FindRoots(leaf, [intermediate1, intermediate2], [root1, root2]));
	}

	[Fact]
	public void with_cycle_between_leaf_and_root() {
		using var rootRsa = RSA.Create();
		using var leafRsa = RSA.Create();
		const string leafSubject = "CN=leaf";

		var root = CreateCertificate(
			ca: true,
			subject: TestUtils.GenerateSubject(),
			keyPair: rootRsa,
			parentInfo: (leafRsa, new X500DistinguishedName(leafSubject)),
			expired: false);

		var intermediate = CreateCertificate(ca: true, parent: root);
		var leaf = CreateCertificate(ca: false, parent: intermediate, subject: leafSubject, keyPair: leafRsa);

		Assert.Empty(
			FindRoots(leaf, [intermediate], [root]));
	}

	[Fact]
	public void with_cycle_between_intermediate_and_root() {
		using var rootRsa = RSA.Create();
		using var intermediateRsa = RSA.Create();
		const string intermediateSubject = "CN=intermediate";

		var root = CreateCertificate(
			ca: true,
			subject: TestUtils.GenerateSubject(),
			keyPair: rootRsa,
			parentInfo: (intermediateRsa, new X500DistinguishedName(intermediateSubject)),
			expired: false);

		var intermediate = CreateCertificate(ca: true, subject: intermediateSubject, parent: root,
			keyPair: intermediateRsa);
		var leaf = CreateCertificate(ca: false, parent: intermediate);

		Assert.Empty(
			FindRoots(leaf, [intermediate], [root]));
	}
}
