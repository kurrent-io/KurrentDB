// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Core.Certificates;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Certificates;

public class publicly_trusted_certificate_overlap {
	private static X509Certificate2 CertWithSans(string commonName, params (string name, string type)[] sans) {
		using var rsa = RSA.Create();
		var certReq = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		if (sans.Length > 0) {
			var sanBuilder = new SubjectAlternativeNameBuilder();
			foreach (var (name, type) in sans) {
				if (type == "DNS")
					sanBuilder.AddDnsName(name);
				else if (type == "IP")
					sanBuilder.AddIpAddress(IPAddress.Parse(name));
			}
			certReq.CertificateExtensions.Add(sanBuilder.Build());
		}
		return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
	}

	[Test]
	public void no_overlap_when_node_cert_san_does_not_match_publicly_trusted_cert_san() {
		var publiclyTrustedCert = CertWithSans("public", ("db.public.local", "DNS"));
		var nodeCert = CertWithSans("node", ("node1.cluster.internal", "DNS"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.IsEmpty(overlaps);
	}

	[Test]
	public void overlap_detected_when_node_cert_dns_san_matches_publicly_trusted_cert_san() {
		var publiclyTrustedCert = CertWithSans("public", ("db.example.com", "DNS"));
		var nodeCert = CertWithSans("node", ("db.example.com", "DNS"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(1, overlaps.Length);
		Assert.AreEqual("db.example.com", overlaps[0]);
	}

	[Test]
	public void overlap_detected_when_publicly_trusted_cert_wildcard_san_covers_node_cert_dns_san() {
		var publiclyTrustedCert = CertWithSans("public", ("*.example.com", "DNS"));
		var nodeCert = CertWithSans("node", ("node1.example.com", "DNS"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(1, overlaps.Length);
		Assert.AreEqual("node1.example.com", overlaps[0]);
	}

	[Test]
	public void ip_sans_on_node_cert_are_ignored_since_ips_are_not_valid_sni() {
		var publiclyTrustedCert = CertWithSans("public", ("127.0.0.1", "IP"));
		var nodeCert = CertWithSans("node", ("127.0.0.1", "IP"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.IsEmpty(overlaps);
	}

	[Test]
	public void dns_san_overlap_reported_even_when_both_certs_also_have_ip_sans() {
		var publiclyTrustedCert = CertWithSans("public", ("db.example.com", "DNS"), ("1.2.3.4", "IP"));
		var nodeCert = CertWithSans("node", ("db.example.com", "DNS"), ("127.0.0.1", "IP"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(1, overlaps.Length);
		Assert.AreEqual("db.example.com", overlaps[0]);
	}

	[Test]
	public void overlap_match_is_case_insensitive() {
		var publiclyTrustedCert = CertWithSans("public", ("DB.Example.COM", "DNS"));
		var nodeCert = CertWithSans("node", ("db.example.com", "DNS"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(1, overlaps.Length);
	}

	[Test]
	public void multiple_overlaps_are_all_reported() {
		var publiclyTrustedCert = CertWithSans("public", ("node1.example.com", "DNS"), ("node2.example.com", "DNS"));
		var nodeCert = CertWithSans("node", ("node1.example.com", "DNS"), ("node2.example.com", "DNS"));

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(2, overlaps.Length);
		CollectionAssert.AreEquivalent(new[] { "node1.example.com", "node2.example.com" }, overlaps);
	}

	[Test]
	public void falls_back_to_cn_when_node_cert_has_no_san_extension() {
		// RFC 6125: CN is consulted only when the SAN extension is absent entirely
		var publiclyTrustedCert = CertWithSans("public", ("db.example.com", "DNS"));
		var nodeCert = CertWithSans("db.example.com"); // no SANs, CN matches

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.AreEqual(1, overlaps.Length);
		Assert.AreEqual("db.example.com", overlaps[0]);
	}

	[Test]
	public void does_not_fall_back_to_cn_when_node_cert_has_non_dns_sans() {
		// SAN extension is present (IP only), so per RFC 6125 the CN is NOT consulted
		// even though there's no DNS SAN.
		var publiclyTrustedCert = CertWithSans("public", ("db.example.com", "DNS"));
		var nodeCert = CertWithSans("db.example.com", ("127.0.0.1", "IP")); // CN matches but SAN is IP-only

		var overlaps = OptionsCertificateProvider.FindNodeCertificateSanOverlaps(publiclyTrustedCert, nodeCert).ToArray();

		Assert.IsEmpty(overlaps);
	}
}
