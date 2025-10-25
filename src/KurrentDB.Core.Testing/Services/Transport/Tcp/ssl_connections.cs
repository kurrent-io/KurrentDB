// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.Tests.Services.Transport.Tcp;

public class ssl_connections {
	private static X509Certificate2 _root, _server, _otherServer, _untrusted;
	public static X509Certificate2 GetRootCertificate() {
		_root ??= GetCertificate("ca", loadKey: false);
		return new X509Certificate2(_root);
	}

	public static X509Certificate2 GetServerCertificate() {
		_server ??= GetCertificate("node1");
		return new X509Certificate2(_server);
	}

	public static X509Certificate2 GetOtherServerCertificate() {
		_otherServer ??= GetCertificate("node2");
		return new X509Certificate2(_otherServer);
	}

	public static X509Certificate2 GetUntrustedCertificate() {
		_untrusted ??= GetCertificate("untrusted");
		return new X509Certificate2(_untrusted);
	}

	private static X509Certificate2 GetCertificate(string name, bool loadKey = true) {
		const string resourcePath = "KurrentDB.Core.Testing.Services.Transport.Tcp.test_certificates";

		var certBytes = LoadResource($"{resourcePath}.{name}.{name}.crt");
		var certificate = X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(certBytes));

		if (!loadKey)
			return certificate;

		var keyBytes = LoadResource($"{resourcePath}.{name}.{name}.key");
		using var rsa = RSA.Create();
		rsa.ImportFromPem(Encoding.UTF8.GetString(keyBytes));

		using X509Certificate2 certWithKey = certificate.CopyWithPrivateKey(rsa);

		// recreate the certificate from a PKCS #12 bundle to work around: https://github.com/dotnet/runtime/issues/23749
		return new X509Certificate2(certWithKey.ExportToPkcs12(), string.Empty, X509KeyStorageFlags.Exportable);
	}

	private static byte[] LoadResource(string resource) {
		using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
		if (resourceStream == null)
			return null;

		using var memStream = new MemoryStream();
		resourceStream.CopyTo(memStream);
		return memStream.ToArray();
	}
}
