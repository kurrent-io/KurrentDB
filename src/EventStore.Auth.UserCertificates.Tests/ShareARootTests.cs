// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

using static EventStore.Auth.UserCertificates.Tests.TestUtils;

namespace EventStore.Auth.UserCertificates.Tests;

public class ShareARootTests {
	private static bool ShareARoot(
		X509Certificate2 leaf1,
		X509Certificate2 leaf2,
		X509Certificate2[]? intermediates,
		X509Certificate2[]? roots) =>
		CertificateUtils.ShareARoot(
			leaf1,
			leaf2,
			intermediates is null ? null : new (intermediates),
			roots is null ? null : new (roots),
			out _,
			out _);

	[Fact]
	public void with_same_root() {
		var root = CreateCertificate(ca: true);
		var leaf1 = CreateCertificate(ca: false, parent: root);
		var leaf2 = CreateCertificate(ca: false, parent: root);
		Assert.True(ShareARoot(leaf1, leaf2, null, [root]));
	}

	[Fact]
	public void with_same_self_signed_root() {
		var selfSignedLeaf = CreateCertificate(ca: true, parent: null);
		var otherLeaf = CreateCertificate(ca: false, parent: selfSignedLeaf);
		Assert.True(ShareARoot(selfSignedLeaf, otherLeaf, null, [selfSignedLeaf]));
	}

	[Fact]
	public void with_different_root() {
		var root1 = CreateCertificate(ca: true);
		var root2 = CreateCertificate(ca: true);
		var leaf1 = CreateCertificate(ca: false, parent: root1);
		var leaf2 = CreateCertificate(ca: false, parent: root2);
		Assert.False(ShareARoot(leaf1, leaf2, null, [root1, root2]));
	}

	[Fact]
	public void with_expired_root() {
		var root = CreateCertificate(ca: true, expired: true);
		var leaf1 = CreateCertificate(ca: false, parent: root);
		var leaf2 = CreateCertificate(ca: false, parent: root);
		Assert.False(ShareARoot(leaf1, leaf2, null, [root]));
	}

	[Fact]
	public void with_same_root_and_intermediate() {
		var root = CreateCertificate(ca: true);
		var intermediate = CreateCertificate(ca: true, parent: root);
		var leaf1 = CreateCertificate(ca: false, parent: intermediate);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate);
		Assert.True(ShareARoot(leaf1, leaf2, [intermediate], [root]));
	}

	[Fact]
	public void with_same_root_and_expired_intermediate() {
		var root = CreateCertificate(ca: true);
		var intermediate = CreateCertificate(ca: true, parent: root, expired: true);
		var leaf1 = CreateCertificate(ca: false, parent: intermediate);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate);
		Assert.False(ShareARoot(leaf1, leaf2, [intermediate], [root]));
	}

	[Fact]
	public void with_same_root_but_different_intermediate() {
		var root = CreateCertificate(ca: true);
		var intermediate1 = CreateCertificate(ca: true, parent: root);
		var intermediate2 = CreateCertificate(ca: true, parent: root);
		var leaf1 = CreateCertificate(ca: false, parent: intermediate1);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);
		Assert.True(ShareARoot(leaf1, leaf2, [intermediate1, intermediate2], [root]));
	}

	[Fact]
	public void with_equivalent_roots() {
		using var rsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rsa);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rsa);
		var leaf1 = CreateCertificate(ca: false, parent: root1);
		var leaf2 = CreateCertificate(ca: false, parent: root2);
		Assert.True(ShareARoot(leaf1, leaf2, null, [root1, root2]));
	}

	[Fact]
	public void with_equivalent_roots_and_intermediates() {
		using var rootRsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa);

		using var intermediateRsa = RSA.Create();
		var intermediate1 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate",
			keyPair: intermediateRsa);
		var intermediate2 = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate",
			keyPair: intermediateRsa);

		var leaf1 = CreateCertificate(ca: false, parent: intermediate1);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);

		Assert.True(ShareARoot(leaf1, leaf2, [intermediate1, intermediate2], [root1, root2]));
	}

	[Fact]
	public void with_equivalent_roots_and_intermediates_but_some_have_expired() {
		using var rootRsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa, expired: true);

		using var intermediateRsa = RSA.Create();
		var intermediate1 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate",
			keyPair: intermediateRsa, expired: true);
		var intermediate2 = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate",
			keyPair: intermediateRsa);

		var leaf1 = CreateCertificate(ca: false, parent: intermediate1);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);

		Assert.True(ShareARoot(leaf1, leaf2, [intermediate1, intermediate2], [root1, root2]));
	}

	[Fact]
	public void with_equivalent_roots_and_intermediates_but_all_have_expired() {
		using var rootRsa = RSA.Create();
		var root1 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa, expired: true);
		var root2 = CreateCertificate(ca: true, subject: "CN=root", keyPair: rootRsa, expired: true);

		using var intermediateRsa = RSA.Create();
		var intermediate1 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate",
			keyPair: intermediateRsa, expired: true);
		var intermediate2 = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate",
			keyPair: intermediateRsa, expired: true);

		var leaf1 = CreateCertificate(ca: false, parent: intermediate1);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);

		Assert.False(ShareARoot(leaf1, leaf2, [intermediate1, intermediate2], [root1, root2]));
	}

	[Fact]
	public void with_a_shared_root_and_a_non_shared_root() {
		var root1 = CreateCertificate(ca: true, subject: "CN=root1");
		var root2 = CreateCertificate(ca: true, subject: "CN=root2");

		using var intermediateRsa = RSA.Create();
		var intermediate1a = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate1",
			keyPair: intermediateRsa);
		var intermediate1b = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate1",
			keyPair: intermediateRsa);
		var intermediate2 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate2");

		var leaf1 = CreateCertificate(ca: false, parent: intermediate1a);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);

		Assert.True(ShareARoot(leaf1, leaf2, [intermediate1a, intermediate1b, intermediate2], [root1, root2]));
	}


	[Fact]
	public void with_a_shared_root_having_an_expired_chain_and_a_non_shared_root() {
		var root1 = CreateCertificate(ca: true, subject: "CN=root1");
		var root2 = CreateCertificate(ca: true, subject: "CN=root2");

		using var intermediateRsa = RSA.Create();
		var intermediate1a = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate1",
			keyPair: intermediateRsa, expired: true);
		var intermediate1b = CreateCertificate(ca: true, parent: root2, subject: "CN=intermediate1",
			keyPair: intermediateRsa);
		var intermediate2 = CreateCertificate(ca: true, parent: root1, subject: "CN=intermediate2");

		var leaf1 = CreateCertificate(ca: false, parent: intermediate1a);
		var leaf2 = CreateCertificate(ca: false, parent: intermediate2);

		Assert.False(ShareARoot(leaf1, leaf2, [intermediate1a, intermediate1b, intermediate2], [root1, root2]));
	}
}
