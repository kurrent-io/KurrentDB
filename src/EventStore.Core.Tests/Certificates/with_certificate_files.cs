// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.Certificates;

public class with_der : with_certificate_chain_of_length_1 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.der";
		_keyPath = $"{PathName}/leaf.key";
		File.WriteAllBytes(_certPath, _leaf.Export(X509ContentType.Cert));
		File.WriteAllText(_keyPath, _leaf.PemPrivateKey());
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_der_and_wrong_key : with_certificate_chain_of_length_1 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.der";
		_keyPath = $"{PathName}/leaf.key";

		using var wrongKey = RSA.Create();
		File.WriteAllBytes(_certPath, _leaf.Export(X509ContentType.Cert));
		File.WriteAllText(_keyPath, wrongKey.ExportRSAPrivateKey().PEM("RSA PRIVATE KEY"));
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(() =>
			CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty)
		);
	}
}

public class with_pem : with_certificate_chain_of_length_1 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.pem";
		_keyPath = $"{PathName}/leaf.key";
		File.WriteAllText(_certPath, _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText(_keyPath, _leaf.PemPrivateKey());
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_pem_and_wrong_key : with_certificate_chain_of_length_1 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.pem";
		_keyPath = $"{PathName}/leaf.key";

		using var wrongKey = RSA.Create();
		File.WriteAllText(_certPath, _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText(_keyPath, wrongKey.ExportRSAPrivateKey().PEM("RSA PRIVATE KEY"));
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(
			() => CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty)
		);
	}
}

public class with_password_protected_pkcs12 : with_certificate_chain_of_length_1 {
	private string _certPath;
	private const string Password = "test$1234";

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.p12";
		File.WriteAllBytes(_certPath, _leaf.ExportToPkcs12(Password));
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, null, Password);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_passwordless_pkcs8_private_key : with_certificate_chain_of_length_1 {
	private string _certPath;
	private string _privateKeyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.pem";
		File.WriteAllText(_certPath, _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		
		_privateKeyPath = $"{PathName}/leaf.p8";
		File.WriteAllText(_privateKeyPath, _leaf.PemPkcs8PrivateKey());
	}

	[Test]
	public void can_load_certificate_and_private_key() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, _privateKeyPath, null);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_password_protected_pkcs8_private_key : with_certificate_chain_of_length_1 {
	private string _certPath;
	private string _privateKeyPath;
	private const string Password = "test$1234";

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.pem";
		File.WriteAllText(_certPath, _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		
		_privateKeyPath = $"{PathName}/leaf.p8";
		File.WriteAllText(_privateKeyPath, _leaf.EncryptedPemPkcs8PrivateKey(Password));
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, _privateKeyPath, null, Password);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_passwordless_pkcs12 : with_certificate_chain_of_length_1 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.p12";
		File.WriteAllBytes(_certPath, _leaf.ExportToPkcs12(Password));
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, null, Password);
		Assert.AreEqual(_leaf, certificate);
		Assert.IsNull(intermediates);
		Assert.True(certificate.HasPrivateKey);
	}
}

public class with_pkcs12_and_wrong_key : with_certificate_chain_of_length_1 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/leaf.p12";

		using var wrongKey = RSA.Create();
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_leaf);
		safeContents.AddShroudedKey(wrongKey, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(() =>
			CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
	}
}

public class with_pem_bundle : with_certificate_chain_of_length_3 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.pem";
		_keyPath = $"{PathName}/leaf.key";
		var leaf = _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE");
		var intermediate = _intermediate.Export(X509ContentType.Cert).PEM("CERTIFICATE");

		File.WriteAllText(_certPath, leaf + intermediate);
		File.WriteAllText(_keyPath, _leaf.PemPrivateKey());
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty);
		Assert.AreEqual(_leaf, certificate);
		Assert.AreEqual(1, intermediates.Count);
		Assert.AreEqual(_intermediate, intermediates[0]);
		Assert.True(certificate.HasPrivateKey);
		Assert.False(intermediates[0].HasPrivateKey);
	}
}

public class with_pem_bundle_and_wrong_key : with_certificate_chain_of_length_3 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.pem";
		_keyPath = $"{PathName}/leaf.key";
		var leaf = _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE");
		var intermediate = _intermediate.Export(X509ContentType.Cert).PEM("CERTIFICATE");

		using var wrongKey = RSA.Create();
		File.WriteAllText(_certPath, leaf + intermediate);
		File.WriteAllText(_keyPath, wrongKey.ExportRSAPrivateKey().PEM("RSA PRIVATE KEY"));
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(
		() => CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty)
		);
	}
}

public class with_wrongly_ordered_pem_bundle : with_certificate_chain_of_length_3 {
	private string _certPath, _keyPath;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.pem";
		_keyPath = $"{PathName}/leaf.key";
		var leaf = _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE");
		var intermediate = _intermediate.Export(X509ContentType.Cert).PEM("CERTIFICATE");

		File.WriteAllText(_certPath, intermediate + leaf);
		File.WriteAllText(_keyPath, _leaf.PemPrivateKey());
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(
		() => CertificateUtils.LoadFromFile(_certPath, _keyPath, string.Empty)
		);
	}
}

public class with_password_protected_pkcs12_bundle : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = "test$1234";

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_leaf);
		safeContents.AddCertificate(_intermediate);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, null, Password);
		Assert.AreEqual(_leaf, certificate);
		Assert.AreEqual(1, intermediates.Count);
		Assert.AreEqual(_intermediate, intermediates[0]);
		Assert.True(certificate.HasPrivateKey);
		Assert.False(intermediates[0].HasPrivateKey);
	}
}

public class with_passwordless_pkcs12_bundle : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_leaf);
		safeContents.AddCertificate(_intermediate);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void can_load_certificate() {
		var (certificate, intermediates) = CertificateUtils.LoadFromFile(_certPath, null, Password);
		Assert.AreEqual(_leaf, certificate);
		Assert.AreEqual(1, intermediates.Count);
		Assert.AreEqual(_intermediate, intermediates[0]);
		Assert.True(certificate.HasPrivateKey);
		Assert.False(intermediates[0].HasPrivateKey);
	}
}

public class with_wrongly_ordered_pkcs12_bundle : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_intermediate);
		safeContents.AddCertificate(_leaf);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(
			() => CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
	}
}

public class with_pkcs12_bundle_having_no_keys : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_leaf);
		safeContents.AddCertificate(_intermediate);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		var ex = Assert.Throws<Exception>(
			() => CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
		Assert.AreEqual(ex!.Message, "No private keys found");
	}
}

public class with_pkcs12_bundle_having_no_certificates : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		var ex = Assert.Throws<Exception>(
			() => CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
		Assert.AreEqual(ex!.Message, "No certificates found");
	}
}

public class with_pkcs12_bundle_having_multiple_keys : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(_leaf.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);

		using var rsa2 = RSA.Create();
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_intermediate);
		safeContents.AddCertificate(_leaf);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		safeContents.AddShroudedKey(rsa2, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		var ex = Assert.Throws<Exception>(
			() => CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
		Assert.AreEqual(ex!.Message, "Multiple private keys found");
	}
}

public class with_pkcs12_bundle_having_wrong_key : with_certificate_chain_of_length_3 {
	private string _certPath;
	private const string Password = null;

	[SetUp]
	public void Setup() {
		_certPath = $"{PathName}/bundle.p12";

		using var rsa = RSA.Create();
		var builder = new Pkcs12Builder();
		var safeContents = new Pkcs12SafeContents();
		var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
		safeContents.AddCertificate(_intermediate);
		safeContents.AddCertificate(_leaf);
		safeContents.AddShroudedKey(rsa, Password, pbeParams);
		builder.AddSafeContentsEncrypted(safeContents, Password, pbeParams);
		builder.SealWithMac(Password, HashAlgorithmName.SHA256, 2048);

		File.WriteAllBytes(_certPath, builder.Encode());
	}

	[Test]
	public void cannot_load_certificate() {
		Assert.Throws<ArgumentException>(
			() => CertificateUtils.LoadFromFile(_certPath, null, Password)
		);
	}
}

public class when_loading_certificates_from_directory : with_certificate_chain_of_length_1 {
	private const string CertsDir = "certs";

	[SetUp]
	public void Setup() {
		Directory.CreateDirectory($"{PathName}/{CertsDir}");
		var p = $"{PathName}/{CertsDir}/leaf";
		File.WriteAllBytes($"{p}.der", _leaf.Export(X509ContentType.Cert));
		File.WriteAllText($"{p}.crt", _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText($"{p}.cer", _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText($"{p}.cert", _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllText($"{p}.pem", _leaf.Export(X509ContentType.Cert).PEM("CERTIFICATE"));
		File.WriteAllBytes($"{p}.p12", _leaf.ExportToPkcs12());
		File.WriteAllBytes($"{p}.pfx", _leaf.ExportToPkcs12());
		File.WriteAllText($"{p}.key", _leaf.PemPrivateKey());
	}


	[Test]
	public void loads_only_certificates_with_accepted_extensions() {
		var certs = CertificateUtils.LoadAllCertificates($"{PathName}/{CertsDir}").ToArray();
		Assert.AreEqual(8 - 3, certs.Length);
		var accepted = new[]{".crt", ".cert", ".cer", ".pem", ".der"};
		Assert.True(certs.All(x => accepted.Contains(new FileInfo(x.fileName).Extension)));
	}
}
