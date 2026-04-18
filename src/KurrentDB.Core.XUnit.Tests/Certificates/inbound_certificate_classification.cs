// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Certificates;

public class inbound_certificate_classification {
	private static readonly X509KeyUsageFlags DefaultKeyUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;

	private static X509Certificate2 GenCert(
		bool serverAuth, bool clientAuth,
		X509KeyUsageFlags? keyUsages = null) {

		using var rsa = RSA.Create();
		var certReq = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

		certReq.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsages ?? DefaultKeyUsages, critical: false));

		if (serverAuth || clientAuth) {
			var oids = new OidCollection();
			if (serverAuth) oids.Add(new Oid("1.3.6.1.5.5.7.3.1"));
			if (clientAuth) oids.Add(new Oid("1.3.6.1.5.5.7.3.2"));
			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
		}

		return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
	}

	// serverAuth and clientAuth
	[InlineData(true,  true,  false, CertificateClassification.Node, "")]
	// serverAuth only (e.g. public CA)
	[InlineData(true,  false, true,  CertificateClassification.Node, "")]
	[InlineData(true,  false, false, CertificateClassification.Unclassified, "Certificate is not a user certificate. Certificate has the serverAuth EKU but not the clientAuth EKU. If you are using a certificate from a public CA that does not include the clientAuth EKU, please see the documentation for the DisableClientAuthEkuValidation configuration option.")]
	// clientAuth only
	[InlineData(false, true,  false, CertificateClassification.User, "")]
	// No EKU extension
	[InlineData(false, false, false, CertificateClassification.Node, "")]
	[Theory]
	public void classify(
		bool serverAuth, bool clientAuth, bool disableClientAuthEkuValidation,
		CertificateClassification expectedProfile, string expectedDescription) {

		var cert = GenCert(serverAuth, clientAuth);
		var profile = cert.ClassifyInboundCertificate(disableClientAuthEkuValidation, out var description);
		Assert.Equal(expectedProfile, profile);
		Assert.Equal(expectedDescription, description);
	}

	[InlineData(X509KeyUsageFlags.None)]
	[InlineData(X509KeyUsageFlags.KeyEncipherment)]
	[InlineData(X509KeyUsageFlags.DigitalSignature)]
	[Theory]
	public void bad_key_usages_are_unknown(X509KeyUsageFlags keyUsages) {
		var cert = GenCert(serverAuth: true, clientAuth: true, keyUsages: keyUsages);
		Assert.Equal(CertificateClassification.Unclassified,
			cert.ClassifyInboundCertificate(disableClientAuthEkuValidation: false, out _));
	}

	[Fact]
	public void key_agreement_instead_of_key_encipherment_is_valid() {
		var cert = GenCert(serverAuth: true, clientAuth: true,
			keyUsages: X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement);
		Assert.Equal(CertificateClassification.Node,
			cert.ClassifyInboundCertificate(disableClientAuthEkuValidation: false, out _));
	}
}
