// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace EventStore.Auth.UserCertificates.Tests;

public class CertificateExtensionsTests {
	private readonly Oid _serverAuth = Oid.FromOidValue("1.3.6.1.5.5.7.3.1", OidGroup.EnhancedKeyUsage);
	private readonly Oid _clientAuth = Oid.FromOidValue("1.3.6.1.5.5.7.3.2", OidGroup.EnhancedKeyUsage);
	private const X509KeyUsageFlags DefaultKeyUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;

	private static X509Certificate2 GenSut(X509KeyUsageFlags keyUsages, OidCollection extendedKeyUsages) {
		using (RSA rsa = RSA.Create()) {
			var certReq =
				new CertificateRequest("CN=hello", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

			var keyUsageExtension = new X509KeyUsageExtension(keyUsages, false);
			var extendedKeyUsageExtension = new X509EnhancedKeyUsageExtension(extendedKeyUsages, false);

			certReq.CertificateExtensions.Add(keyUsageExtension);

			if (extendedKeyUsages.Count != 0)
				certReq.CertificateExtensions.Add(extendedKeyUsageExtension);

			return certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1),
				DateTimeOffset.UtcNow.AddMonths(1));
		}
	}

	private static OidCollection GenOids(params Oid[] oids) {
		var oidCollection = new OidCollection();
		foreach (var oid in oids) {
			oidCollection.Add(oid);
		}

		return oidCollection;
	}

	[Fact]
	public void certificate_with_client_auth_eku() {
		var sut = GenSut(
			keyUsages: DefaultKeyUsages,
			extendedKeyUsages: GenOids(_clientAuth));

		Assert.True(sut.IsClientCertificate(out _));
		Assert.False(sut.IsServerCertificate(out _));
	}

	[Fact]
	public void certificate_with_client_and_server_auth_eku() {
		var sut = GenSut(
			keyUsages: DefaultKeyUsages,
			extendedKeyUsages: GenOids(_clientAuth, _serverAuth));

		Assert.True(sut.IsServerCertificate(out _));
		Assert.True(sut.IsClientCertificate(out _));
	}

	[Fact]
	public void certificate_with_server_auth_eku_only() {
		var sut = GenSut(
			keyUsages: DefaultKeyUsages,
			extendedKeyUsages: GenOids(_serverAuth));

		// historically, server certificates also have the clientAuth EKU
		Assert.False(sut.IsServerCertificate(out _));
		Assert.False(sut.IsClientCertificate(out _));
	}

	[Fact]
	public void certificate_with_no_ekus() {
		var sut = GenSut(
			keyUsages: DefaultKeyUsages,
			extendedKeyUsages: GenOids());

		Assert.True(sut.IsServerCertificate(out _));
		Assert.False(sut.IsClientCertificate(out _));
	}

	[Fact]
	public void certificate_with_no_key_usage() {
		var sut = GenSut(
			keyUsages: X509KeyUsageFlags.None,
			extendedKeyUsages: GenOids(_clientAuth, _serverAuth));

		Assert.False(sut.IsClientCertificate(out _));
		Assert.False(sut.IsServerCertificate(out _));
	}

	[Fact]
	public void certificate_with_missing_key_usage() {
		var sut = GenSut(
			keyUsages: X509KeyUsageFlags.KeyEncipherment,
			extendedKeyUsages: GenOids(_clientAuth, _serverAuth));

		Assert.False(sut.IsClientCertificate(out _));
		Assert.False(sut.IsServerCertificate(out _));
	}

	[Fact]
	public void certificate_with_key_agreement_instead_of_key_encipherment_key_usage() {
		var sut = GenSut(
			keyUsages: X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement,
			extendedKeyUsages: GenOids(_clientAuth, _serverAuth));

		Assert.True(sut.IsClientCertificate(out _));
		Assert.True(sut.IsServerCertificate(out _));
	}
}
