// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace KurrentDB.Common.DevCertificates;

/// <summary>
/// Manages loading and saving dev certificates to/from PFX files on disk.
/// </summary>
public static class DevCertificateFile {
	/// <summary>
	/// Tries to load a valid dev certificate from a PFX file.
	/// Returns null if the file doesn't exist, the cert is expired, or it's not a dev certificate.
	/// </summary>
	public static X509Certificate2 TryLoad(string pfxPath) {
		if (!File.Exists(pfxPath))
			return null;

		X509Certificate2 cert;
		try {
			cert = new X509Certificate2(pfxPath, (string)null, X509KeyStorageFlags.Exportable);
		} catch {
			return null;
		}

		if (CertificateManager.IsHttpsDevelopmentCertificate(cert) &&
			cert.NotAfter > DateTimeOffset.UtcNow &&
			cert.HasPrivateKey) {
			return cert;
		}

		cert.Dispose();
		return null;
	}

	/// <summary>
	/// Writes the public certificate as a PEM-encoded .crt file alongside the PFX.
	/// The .crt file can be shared with clients for trust configuration.
	/// </summary>
	public static void WritePublicCertificate(X509Certificate2 certificate, string pfxPath) {
		var crtPath = Path.ChangeExtension(pfxPath, ".crt");
		if (string.Equals(crtPath, pfxPath, StringComparison.OrdinalIgnoreCase))
			crtPath = pfxPath + ".crt";
		var pem = new string(PemEncoding.Write("CERTIFICATE", certificate.Export(X509ContentType.Cert)));
		File.WriteAllText(crtPath, pem, Encoding.ASCII);
	}
}
