// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace EventStore.Core.Tests.Certificates;

public class with_valid_chain_of_length_2 : with_certificate_chain_of_length_2 {
	[Test]
	public void builds_with_no_error() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.NoError);
	}
}

public class with_valid_chain_of_length_3 : with_certificate_chain_of_length_3 {
	[Test]
	public void builds_with_no_error() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(_intermediate),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.NoError);
	}
}

public class with_expired_root_certificate : with_certificate_chain_of_length_3 {
	public with_expired_root_certificate() : base(rootExpired: true) { }

	[Test]
	public void builds_with_not_time_valid() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(_intermediate),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.NotTimeValid);
	}
}

public class with_expired_intermediate_certificate : with_certificate_chain_of_length_3 {
	public with_expired_intermediate_certificate() : base(intermediateExpired: true) { }
	[Test]
	public void builds_with_not_time_valid() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(_intermediate),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.NotTimeValid);
	}
}

public class with_expired_leaf_certificate : with_certificate_chain_of_length_3 {
	public with_expired_leaf_certificate() : base(leafExpired: true) { }

	[Test]
	public void builds_with_not_time_valid() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(_intermediate),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.NotTimeValid);
	}
}

public class without_intermediate_certificate : with_certificate_chain_of_length_3 {
	[Test]
	public void builds_with_partial_chain() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(),
			new X509Certificate2Collection(_root), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.PartialChain);
	}
}

public class without_root_certificate : with_certificate_chain_of_length_3 {
	[Test]
	public void builds_with_partial_chain() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(_intermediate),
			new X509Certificate2Collection(), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.PartialChain);
	}
}

public class with_untrusted_root : with_certificate_chain_of_length_3 {
	[Test]
	public void builds_with_untrusted_root() {
		var chainStatus = CertificateUtils.BuildChain(
			_leaf,
			new X509Certificate2Collection(new []{_intermediate, _root}),
			new X509Certificate2Collection(), out _);
		Assert.True(chainStatus == X509ChainStatusFlags.UntrustedRoot);
	}
}
