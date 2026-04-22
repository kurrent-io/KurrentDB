// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Certificates;

public class with_no_publicly_trusted_certificate_configured {
	[Test]
	public void load_publicly_trusted_certificate_returns_null() {
		var options = new ClusterVNodeOptions();
		var result = options.LoadPubliclyTrustedCertificate();
		Assert.IsNull(result);
	}
}

public class with_publicly_trusted_certificate_file : with_certificate_chain_of_length_1 {
	private string _certPath;
	private const string Password = "test$1234";

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/public.p12";
		File.WriteAllBytes(_certPath, _leaf.ExportToPkcs12(Password));
	}

	[Test]
	public void load_publicly_trusted_certificate_returns_certificate() {
		var options = new ClusterVNodeOptions {
			CertificateFile = new() {
				PubliclyTrustedCertificateFile = _certPath,
				PubliclyTrustedCertificatePassword = Password
			}
		};
		var result = options.LoadPubliclyTrustedCertificate();
		Assert.IsNotNull(result);
		Assert.AreEqual(_leaf, result.Value.certificate);
		Assert.IsNull(result.Value.intermediates);
		Assert.True(result.Value.certificate.HasPrivateKey);
	}
}

public class with_publicly_trusted_certificate_bundle : with_certificate_chain_of_length_3 {
	private string _certPath;
	private string _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/public_fullchain.pem";
		_keyPath = $"{PathName}/public.key";

		// write leaf followed by intermediate — a PEM bundle as produced by publicly-trusted CAs like Let's Encrypt
		File.WriteAllText(
			_certPath,
			_leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE")
				+ _intermediate.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText(_keyPath, _leaf.PemPrivateKey());
	}

	[Test]
	public void load_publicly_trusted_certificate_returns_certificate_and_intermediates() {
		var options = new ClusterVNodeOptions {
			CertificateFile = new() {
				PubliclyTrustedCertificateFile = _certPath,
				PubliclyTrustedCertificatePrivateKeyFile = _keyPath
			}
		};
		var result = options.LoadPubliclyTrustedCertificate();
		Assert.IsNotNull(result);
		Assert.AreEqual(_leaf, result.Value.certificate);
		Assert.IsNotNull(result.Value.intermediates);
		Assert.AreEqual(1, result.Value.intermediates.Count);
		Assert.AreEqual(_intermediate, result.Value.intermediates[0]);
	}
}
