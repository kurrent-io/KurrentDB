// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.DevCertificates;
using KurrentDB.Common.Utils;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Certificates;

public class dev_cert_file_tests : DirectoryPerTest<dev_cert_file_tests> {
	private const string DevCertOid = "1.3.6.1.4.1.43941.1.1.1";

	/// <summary>
	/// Creates a dev certificate PFX file. The key and cert are created and exported
	/// in the same scope so the private key is available for PFX export.
	/// Returns the thumbprint of the saved certificate.
	/// </summary>
	private (string pfxPath, string thumbprint) CreateDevCertPfx(
		string fileName, DateTimeOffset notBefore, DateTimeOffset notAfter) {
		using var key = RSA.Create(2048);
		var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509Extension(
			new AsnEncodedData(new Oid(DevCertOid), new byte[] { 2 }),
			critical: false));
		using var cert = request.CreateSelfSigned(notBefore, notAfter);
		var pfxPath = Fixture.GetFilePathFor(fileName);
		File.WriteAllBytes(pfxPath, cert.ExportToPkcs12(string.Empty));
		return (pfxPath, cert.Thumbprint);
	}

	private string CreateNonDevCertPfx(string fileName, DateTimeOffset notBefore, DateTimeOffset notAfter) {
		using var key = RSA.Create(2048);
		var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using var cert = request.CreateSelfSigned(notBefore, notAfter);
		var pfxPath = Fixture.GetFilePathFor(fileName);
		File.WriteAllBytes(pfxPath, cert.ExportToPkcs12(string.Empty));
		return pfxPath;
	}

	[Fact]
	public void saves_cert_and_writes_crt_alongside() {
		var (pfxPath, _) = CreateDevCertPfx("dev.pfx",
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));
		using var cert = new X509Certificate2(pfxPath, string.Empty,
			X509KeyStorageFlags.Exportable);

		DevCertificateFile.WritePublicCertificate(cert, pfxPath);

		var crtPath = Path.ChangeExtension(pfxPath, ".crt");
		Assert.True(File.Exists(pfxPath), "PFX file should exist");
		Assert.True(File.Exists(crtPath), ".crt file should exist");

		var crtContent = File.ReadAllText(crtPath);
		Assert.StartsWith("-----BEGIN CERTIFICATE-----", crtContent);
		Assert.Contains("-----END CERTIFICATE-----", crtContent);
	}

	[Fact]
	public void crt_file_can_be_loaded_as_certificate() {
		var (pfxPath, expectedThumbprint) = CreateDevCertPfx("dev.pfx",
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));
		using var cert = new X509Certificate2(pfxPath, string.Empty,
			X509KeyStorageFlags.Exportable);

		DevCertificateFile.WritePublicCertificate(cert, pfxPath);

		var crtPath = Path.ChangeExtension(pfxPath, ".crt");
		using var loaded = new X509Certificate2(crtPath);
		Assert.Equal(expectedThumbprint, loaded.Thumbprint);
	}

	[Fact]
	public void loads_valid_cert_from_pfx() {
		var (pfxPath, expectedThumbprint) = CreateDevCertPfx("dev.pfx",
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));

		using var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.NotNull(loaded);
		Assert.Equal(expectedThumbprint, loaded.Thumbprint);
		Assert.True(CertificateManager.IsHttpsDevelopmentCertificate(loaded));
	}

	[Fact]
	public void returns_null_for_expired_cert() {
		var (pfxPath, _) = CreateDevCertPfx("expired.pfx",
			DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddMonths(-1));

		var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.Null(loaded);
	}

	[Fact]
	public void returns_null_for_non_dev_cert() {
		var pfxPath = CreateNonDevCertPfx("non-dev.pfx",
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));

		var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.Null(loaded);
	}

	[Fact]
	public void returns_null_when_file_does_not_exist() {
		var pfxPath = Fixture.GetFilePathFor("missing.pfx");

		var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.Null(loaded);
	}

	[Fact]
	public void returns_null_for_corrupted_file() {
		var pfxPath = Fixture.GetFilePathFor("corrupted.pfx");
		File.WriteAllText(pfxPath, "this is not a certificate");

		var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.Null(loaded);
	}

	[Fact]
	public void returns_null_for_cert_without_private_key() {
		// Create a dev cert, export only the public part to PFX
		using var key = RSA.Create(2048);
		var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509Extension(
			new AsnEncodedData(new Oid(DevCertOid), new byte[] { 2 }),
			critical: false));
		using var cert = request.CreateSelfSigned(
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));

		// Export public cert only (no private key) as DER, then wrap in PFX without key
		var pfxPath = Fixture.GetFilePathFor("no-key.pfx");
		var publicOnly = new X509Certificate2(cert.Export(X509ContentType.Cert));
		File.WriteAllBytes(pfxPath, publicOnly.Export(X509ContentType.Pfx));
		publicOnly.Dispose();

		var loaded = DevCertificateFile.TryLoad(pfxPath);

		Assert.Null(loaded);
	}

	[Fact]
	public void write_public_cert_does_not_overwrite_pfx_when_path_ends_with_crt() {
		var (pfxPath, _) = CreateDevCertPfx("dev.crt",
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(1));
		var originalBytes = File.ReadAllBytes(pfxPath);

		using var cert = new X509Certificate2(pfxPath, string.Empty,
			X509KeyStorageFlags.Exportable);
		DevCertificateFile.WritePublicCertificate(cert, pfxPath);

		// PFX file should not have been overwritten
		Assert.Equal(originalBytes, File.ReadAllBytes(pfxPath));
		// .crt should be written to a safe alternative path
		Assert.True(File.Exists(pfxPath + ".crt"));
	}
}
