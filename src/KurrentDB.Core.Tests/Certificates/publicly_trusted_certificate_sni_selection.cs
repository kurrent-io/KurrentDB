// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Core.Certificates;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Certificates;

public class publicly_trusted_certificate_sni_selection {
	private static X509Certificate2 CertWithDnsSan(params string[] dnsNames) {
		using var rsa = RSA.Create();
		var certReq = new CertificateRequest("CN=public", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var sanBuilder = new SubjectAlternativeNameBuilder();
		foreach (var dnsName in dnsNames)
			sanBuilder.AddDnsName(dnsName);
		certReq.CertificateExtensions.Add(sanBuilder.Build());
		return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
	}

	[Test]
	public void does_not_serve_when_publicly_trusted_certificate_is_null() {
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(null, "db.example.com"));
	}

	[Test]
	public void does_not_serve_when_sni_is_null() {
		var cert = CertWithDnsSan("db.example.com");
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(cert, null));
	}

	[Test]
	public void does_not_serve_when_sni_is_empty() {
		var cert = CertWithDnsSan("db.example.com");
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(cert, string.Empty));
	}

	[Test]
	public void serves_when_sni_matches_san_exactly() {
		var cert = CertWithDnsSan("db.example.com");
		Assert.True(PubliclyTrustedCertificateSelector.ShouldServe(cert, "db.example.com"));
	}

	[Test]
	public void does_not_serve_when_sni_does_not_match_san() {
		var cert = CertWithDnsSan("db.example.com");
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(cert, "other.example.com"));
	}

	[Test]
	public void serves_when_sni_matches_any_of_multiple_sans() {
		var cert = CertWithDnsSan("db.example.com", "api.example.com");
		Assert.True(PubliclyTrustedCertificateSelector.ShouldServe(cert, "api.example.com"));
	}

	[Test]
	public void serves_when_sni_matches_wildcard_san() {
		var cert = CertWithDnsSan("*.example.com");
		Assert.True(PubliclyTrustedCertificateSelector.ShouldServe(cert, "foo.example.com"));
	}

	[Test]
	public void does_not_serve_when_sni_has_extra_labels_under_wildcard_san() {
		var cert = CertWithDnsSan("*.example.com");
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(cert, "sub.foo.example.com"));
	}

	[Test]
	public void does_not_serve_when_sni_is_bare_domain_of_wildcard_san() {
		// "*.example.com" matches one label to the left of example.com, not the bare domain
		var cert = CertWithDnsSan("*.example.com");
		Assert.False(PubliclyTrustedCertificateSelector.ShouldServe(cert, "example.com"));
	}

	[Test]
	public void match_is_case_insensitive() {
		var cert = CertWithDnsSan("DB.Example.COM");
		Assert.True(PubliclyTrustedCertificateSelector.ShouldServe(cert, "db.example.com"));
	}
}
