// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
