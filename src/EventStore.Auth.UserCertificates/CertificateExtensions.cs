// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Auth.UserCertificates;

public static class CertificateExtensions {
	private static bool TryGetKeyUsages(
		this X509Certificate2 certificate,
		out X509KeyUsageFlags keyUsages,
		out bool hasExtendedKeyUsage,
		out Oid[] extKeyUsages,
		out string failReason) {

		keyUsages = X509KeyUsageFlags.None;
		hasExtendedKeyUsage = false;
		extKeyUsages = [];
		failReason = "";

		X509ExtensionCollection extensions;
		try {
			extensions = certificate.Extensions;
		} catch (CryptographicException ex) {
			failReason = ex.Message;
			return false;
		}

		foreach (var extension in extensions) {
			switch (extension.Oid?.Value)
			{
				case "2.5.29.15": // Oid for Key Usage extension
					var keyUsageExt = (X509KeyUsageExtension)extension;
					keyUsages |= keyUsageExt.KeyUsages;
					break;
				case "2.5.29.37": // Oid for Extended Key Usage extension
					hasExtendedKeyUsage = true;
					var enhancedKeyUsageExt = (X509EnhancedKeyUsageExtension)extension;
					extKeyUsages = new Oid[enhancedKeyUsageExt.EnhancedKeyUsages.Count];
					if (extKeyUsages.Length > 0)
						enhancedKeyUsageExt.EnhancedKeyUsages.CopyTo(extKeyUsages, 0);
					break;
			}
		}

		return true;
	}

	private static bool HasCorrectKeyUsages(X509KeyUsageFlags keyUsageFlags, out string failReason) {
		if (!keyUsageFlags.HasFlag(X509KeyUsageFlags.DigitalSignature)) {
			failReason = "Missing key usage: Digital Signature";
			return false;
		}

		if (!keyUsageFlags.HasFlag(X509KeyUsageFlags.KeyEncipherment) &&
		    !keyUsageFlags.HasFlag(X509KeyUsageFlags.KeyAgreement)) {
			failReason = "Missing key usage: Key Encipherment and/or Key Agreement";
			return false;
		}

		failReason = string.Empty;
		return true;
	}

	private static bool HasServerAuthExtendedKeyUsage(IEnumerable<Oid> extendedKeyUsages, out string failReason) {
		if (extendedKeyUsages.All(oid => oid.Value != "1.3.6.1.5.5.7.3.1")) { // serverAuth
			failReason = "Missing extended key usage: Server Authentication";
			return false;
		}

		failReason = string.Empty;
		return true;
	}

	private static bool HasClientAuthExtendedKeyUsage(IEnumerable<Oid> extendedKeyUsages, out string failReason) {
		if (extendedKeyUsages.All(oid => oid.Value != "1.3.6.1.5.5.7.3.2")) { // clientAuth
			failReason = "Missing extended key usage: Client Authentication";
			return false;
		}

		failReason = string.Empty;
		return true;
	}

	public static bool IsServerCertificate(this X509Certificate2 certificate, out string failReason) {
		if (!certificate.TryGetKeyUsages(out var keyUsages, out var hasExtKeyUsagesExtension, out var extKeyUsages, out failReason))
			return false;

		if (!HasCorrectKeyUsages(keyUsages, out failReason))
			return false;

		// rfc5280 section-4.2.1.12: extended key usages (EKUs) only have to be enforced
		// if the extension is present at all. here, we don't enforce them for server
		// certificates for backwards compatibility. however, this also implies that we
		// _need_ the EKUs to be present for other types of certificates (e.g user certificates)
		// as otherwise it would cause ambiguity when trying to determine the certificate type.
		if (hasExtKeyUsagesExtension) {
			if (!HasServerAuthExtendedKeyUsage(extKeyUsages, out failReason))
				return false;

			// historically, server certificates also have the clientAuth EKU
			if (!HasClientAuthExtendedKeyUsage(extKeyUsages, out failReason))
				return false;
		}

		failReason = string.Empty;
		return true;
	}

	public static bool IsClientCertificate(this X509Certificate2 certificate, out string failReason) {
		if (!certificate.TryGetKeyUsages(out var keyUsages, out _, out var extKeyUsages, out failReason))
			return false;

		if (!HasCorrectKeyUsages(keyUsages, out failReason))
			return false;

		if (!HasClientAuthExtendedKeyUsage(extKeyUsages, out failReason))
			return false;

		failReason = string.Empty;
		return true;
	}

	public static string GetCommonName(this X509Certificate2 certificate) => certificate.GetNameInfo(X509NameType.SimpleName, false);
}
