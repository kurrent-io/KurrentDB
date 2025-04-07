// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Core.Tests.Certificates;

public static class X509Certificate2Extensions {
	public static string PemPrivateKey(this X509Certificate2 certificate) {
		return certificate.GetRSAPrivateKey()!.ExportRSAPrivateKey().PEM("RSA PRIVATE KEY");
	}

	public static string PemPkcs8PrivateKey(this X509Certificate2 certificate) {
		return certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKey().PEM("PRIVATE KEY");
	}

	public static string EncryptedPemPkcs8PrivateKey(this X509Certificate2 certificate, string password) {
		return certificate.GetRSAPrivateKey()!.ExportEncryptedPkcs8PrivateKey(password.AsSpan(), new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA1, 1)).PEM("ENCRYPTED PRIVATE KEY");
	}
}
