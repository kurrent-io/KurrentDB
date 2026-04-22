// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Certificates;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Certificates;

public class system_trust_store_detection {
	[TestCase("/etc/ssl/certs")]
	[TestCase("/etc/ssl/certs/")]
	[TestCase("/etc/pki/ca-trust/extracted/pem")]
	[TestCase("/etc/pki/tls/certs")]
	[TestCase("/usr/local/share/ca-certificates")]
	public void identifies_well_known_system_trust_store_paths(string path) {
		Assert.True(OptionsCertificateProvider.IsSystemTrustStorePath(path));
	}

	[TestCase("/etc/kurrent/trusted_roots")]
	[TestCase("/etc/ssl/my-internal-ca")]
	[TestCase("/opt/kurrent/ca")]
	[TestCase("./trusted-roots")]
	[TestCase("")]
	[TestCase(null)]
	public void does_not_flag_user_configured_paths(string path) {
		Assert.False(OptionsCertificateProvider.IsSystemTrustStorePath(path));
	}

	[Test]
	public void path_match_is_case_insensitive() {
		Assert.True(OptionsCertificateProvider.IsSystemTrustStorePath("/ETC/SSL/certs"));
	}

	[TestCase("Root")]
	[TestCase("root")]
	[TestCase("AuthRoot")]
	public void identifies_system_trust_store_names(string storeName) {
		Assert.True(OptionsCertificateProvider.IsSystemTrustStoreName(storeName));
	}

	[TestCase("My")]
	[TestCase("CertificateAuthority")]
	[TestCase("Disallowed")]
	[TestCase("TrustedPeople")]
	[TestCase("")]
	[TestCase(null)]
	public void does_not_flag_non_trust_anchor_store_names(string storeName) {
		Assert.False(OptionsCertificateProvider.IsSystemTrustStoreName(storeName));
	}
}
