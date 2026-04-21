// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Core.Certificates;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Certificates;

public class OptionsCertificateProviderTests(DirectoryFixture<OptionsCertificateProviderTests> fixture) :
	IClassFixture<DirectoryFixture<OptionsCertificateProviderTests>> {

	private static X509Certificate2 CreateCert(
		string subject,
		bool ca = false,
		X509Certificate2 parent = null,
		bool clientAuthEKU = false,
		bool serverAuthEKU = false) {

		var rsa = RSA.Create();
		var certReq = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

		if (ca)
			certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

		if (clientAuthEKU || serverAuthEKU) {
			certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

			var oids = new OidCollection();
			if (serverAuthEKU) oids.Add(new Oid("1.3.6.1.5.5.7.3.1"));
			if (clientAuthEKU) oids.Add(new Oid("1.3.6.1.5.5.7.3.2"));
			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
		}

		var sanBuilder = new SubjectAlternativeNameBuilder();
		sanBuilder.AddIpAddress(IPAddress.Loopback);
		certReq.CertificateExtensions.Add(sanBuilder.Build());

		X509Certificate2 cert;
		if (parent == null) {
			cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
		} else {
			var parentKey = parent.GetRSAPrivateKey()!;
			var generator = X509SignatureGenerator.CreateForRSA(parentKey, RSASignaturePadding.Pkcs1);
			cert = certReq.Create(parent.SubjectName, generator,
				DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1),
				BitConverter.GetBytes(Random.Shared.Next())).CopyWithPrivateKey(rsa);
		}

		// re-import via PFX so the cert owns its private key independently, and
		// flag the key as exportable so the cert can be exported again later (e.g. to write it to disk)
		return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string)null, X509KeyStorageFlags.Exportable);
	}

	private string WriteCertToFile(X509Certificate2 cert, string fileName) {
		var path = fixture.GetFilePathFor(fileName);
		File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
		return path;
	}

	private static X509Certificate2 StripPrivateKey(X509Certificate2 cert) =>
		new(cert.Export(X509ContentType.Cert));

	private ClusterVNodeOptions BuildOptions(
		X509Certificate2 nodeCert,
		X509Certificate2 rootCert,
		X509Certificate2 nodeClientCert = null,
		string reservedNodeCN = null) {

		var options = new ClusterVNodeOptions {
			ServerCertificate = nodeCert,
			TrustedRootCertificates = new X509Certificate2Collection(StripPrivateKey(rootCert)),
			Certificate = new ClusterVNodeOptions.CertificateOptions {
				CertificateReservedNodeCommonName = reservedNodeCN ?? string.Empty,
				TrustedRootCertificatesPath = fixture.Directory,
			},
		};

		if (nodeClientCert != null) {
			var certPath = WriteCertToFile(nodeClientCert, $"node-client-{Guid.NewGuid()}.pfx");
			options = options with {
				NodeClientCertificateFile = new ClusterVNodeOptions.NodeClientCertificateFileOptions {
					NodeClientCertificateFile = certPath,
				},
			};
			// also write the root to the temp dir so the node client trusted-root fallback can pick it up
			File.WriteAllBytes(fixture.GetFilePathFor($"root-{Guid.NewGuid()}.crt"), rootCert.Export(X509ContentType.Cert));
		}

		return options;
	}

	[Fact]
	public void insecure_mode_skips_loading() {
		var options = new ClusterVNodeOptions {
			Application = new ClusterVNodeOptions.ApplicationOptions { Insecure = true },
		};
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		Assert.Equal(LoadCertificateResult.Skipped, result);
		Assert.Null(sut.Certificate);
	}

	[Fact]
	public void single_cert_mode_loads_successfully() {
		var root = CreateCert("CN=test-ca", ca: true);
		var node = CreateCert("CN=eventstoredb-node", parent: root, serverAuthEKU: true, clientAuthEKU: true);
		var options = BuildOptions(node, root);
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		Assert.Equal(LoadCertificateResult.Success, result);
		Assert.Equal(node.Thumbprint, sut.Certificate.Thumbprint);
		// in single cert mode, node client cert is the same as the main cert
		Assert.Equal(node.Thumbprint, sut.NodeClientCertificate.Thumbprint);
		Assert.Equal("eventstoredb-node", sut.GetReservedNodeCommonName());
	}

	[Fact]
	public void dual_cert_mode_loads_successfully() {
		var root = CreateCert("CN=test-ca", ca: true);
		var node = CreateCert("CN=kurrentdb.example.com", parent: root, serverAuthEKU: true);
		var nodeClient = CreateCert("CN=eventstoredb-node", parent: root, serverAuthEKU: true, clientAuthEKU: true);
		var options = BuildOptions(node, root, nodeClient);
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		Assert.Equal(LoadCertificateResult.Success, result);
		Assert.Equal(node.Thumbprint, sut.Certificate.Thumbprint);
		Assert.Equal(nodeClient.Thumbprint, sut.NodeClientCertificate.Thumbprint);
		// reserved CN auto-derived from the node client cert (not the main cert)
		Assert.Equal("eventstoredb-node", sut.GetReservedNodeCommonName());
	}

	[Fact]
	public void dual_cert_mode_node_client_cert_with_clientauth_only_fails_classification() {
		var root = CreateCert("CN=test-ca", ca: true);
		var node = CreateCert("CN=eventstoredb-node", parent: root, serverAuthEKU: true, clientAuthEKU: true);
		// clientAuth-only classifies as User, not Node
		var nodeClient = CreateCert("CN=eventstoredb-node", parent: root, clientAuthEKU: true);
		var options = BuildOptions(node, root, nodeClient);
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		Assert.Equal(LoadCertificateResult.VerificationFailed, result);
	}

	[Fact]
	public void reserved_node_cn_mismatch_fails() {
		var root = CreateCert("CN=test-ca", ca: true);
		var node = CreateCert("CN=something-else", parent: root, serverAuthEKU: true, clientAuthEKU: true);
		var options = BuildOptions(node, root, reservedNodeCN: "eventstoredb-node");
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		Assert.Equal(LoadCertificateResult.VerificationFailed, result);
	}

	[Fact]
	public void reserved_node_cn_matches_node_client_cert_in_dual_cert_mode() {
		var root = CreateCert("CN=test-ca", ca: true);
		// main cert has a different CN — expected in dual mode (public CA hostname)
		var node = CreateCert("CN=kurrentdb.example.com", parent: root, serverAuthEKU: true);
		var nodeClient = CreateCert("CN=eventstoredb-node", parent: root, serverAuthEKU: true, clientAuthEKU: true);
		var options = BuildOptions(node, root, nodeClient, reservedNodeCN: "eventstoredb-node");
		var sut = new OptionsCertificateProvider();

		var result = sut.LoadCertificates(options);

		// reserved CN matches the node client cert, not the main cert — should succeed
		Assert.Equal(LoadCertificateResult.Success, result);
	}

	[Fact]
	public void get_reserved_node_common_name_throws_before_load() {
		var sut = new OptionsCertificateProvider();

		Assert.Throws<InvalidOperationException>(() => sut.GetReservedNodeCommonName());
	}
}
