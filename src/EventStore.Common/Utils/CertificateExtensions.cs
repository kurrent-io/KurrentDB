using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace EventStore.Common.Utils {
	public static class CertificateExtensions {
		public static IEnumerable<(string name, string type)> GetSubjectAlternativeNames(this X509Certificate2 certificate) {
			// Implemented based on RFC 5280 (https://datatracker.ietf.org/doc/html/rfc5280)
			// - Reads IP addresses and DNS names from the Subject Alternative Names extension
			// - Does not support other name types yet

			X509ExtensionCollection extensions;
			try {
				extensions = certificate.Extensions;
			} catch (CryptographicException) {
				return null;
			}

			var sans = new List<(string, string)>();
			foreach (var extension in extensions) {
				if (extension.Oid?.Value != "2.5.29.17")
					continue; // Oid for Subject Alternative Names extension
				var asnReader = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
				while (asnReader.HasData) {
					Asn1Tag tag;
					try {
						tag = asnReader.PeekTag();
					} catch (AsnContentException) {
						break;
					}

					switch (tag.TagValue) {
						case 2: // dNSName [2] IA5String
							sans.Add((asnReader.ReadCharacterString(UniversalTagNumber.IA5String, tag), CertificateNameType.DnsName));
							break;
						case 7: // iPAddress [7] OCTET STRING
							sans.Add((new IPAddress(asnReader.ReadOctetString(tag)).ToString(), CertificateNameType.IpAddress));
							break;
						default:
							asnReader.ReadEncodedValue();
							break;
					}
				}
			}

			return sans;
		}

		public static bool MatchesName(this X509Certificate2 certificate, string name) {
			// Implemented based on RFC 6125 (https://datatracker.ietf.org/doc/html/rfc6125) with the following changes:
			// - Does not support SRV-ID and URI-ID identifier types yet
			// - Partial wildcard support is not implemented since it has been deprecated in most major browsers

			var sans = GetSubjectAlternativeNames(certificate).ToArray();
			if (sans.Length > 0)
				return sans.Any(san => MatchesName(san.name, san.type, name));

			var cn = GetCommonName(certificate);
			return cn != null && MatchesName(cn, CertificateNameType.DnsName, name);
		}

		public static bool ClientCertificateMatchesName(this X509Certificate2 clientCertificate, string name) {
			// This method, as a whole, is not based on any standard and is specific to EventStoreDB.
			// It matches a client certificate's CN against a name as follows:
			// i)  do an exact (case-insensitive) match if the CN is a wildcard name, otherwise
			// ii) do an RFC 6125 compliant match (with the implementation limitations mentioned above)
			//
			// Basic rules:
			// CN = *.test.com MUST match with name = *.test.com
			// CN = *.test.com MUST NOT match with name = abc.test.com
			// CN = abc.test.com MUST match with name = *.test.com
			// CN = abc.test.com MUST match with name = abc.test.com

			var cn = clientCertificate.GetCommonName();

			// if the CN is a wildcard name, do an exact (case-insensitive) match
			// as a standard RFC 6125 compliant match of two wildcard names will fail
			if (cn.IsWildcardCertificateName())
				return cn.EqualsOrdinalIgnoreCase(name);

			// otherwise, do a standard RFC 6125 compliant name match
			return MatchesName(name, CertificateNameType.DnsName, cn);
		}

		public static string GetCommonName(this X509Certificate2 certificate) => certificate.GetNameInfo(X509NameType.SimpleName, false);

		// FIPS compliant PKCS #12 bundle creation
		public static byte[] ExportToPkcs12(this X509Certificate2 certificate, string password = null) {
			password ??= string.Empty;

			using var rsa = RSA.Create();
			rsa.ImportRSAPrivateKey(certificate.GetRSAPrivateKey()!.ExportRSAPrivateKey(), out _);
			var builder = new Pkcs12Builder();
			var safeContents = new Pkcs12SafeContents();
			var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
			safeContents.AddCertificate(certificate);
			safeContents.AddShroudedKey(rsa, password, pbeParams);
			builder.AddSafeContentsEncrypted(safeContents, password, pbeParams);
			builder.SealWithMac(password, HashAlgorithmName.SHA256, 2048);

			return builder.Encode();
		}

		private static bool HasNonAsciiChars(string s) => s.Any(t => t > 127);

		private static bool IsInternationalizedDomainNameLabel(string s) {
			const string ACEPrefix = "xn--";
			return HasNonAsciiChars(s) || s.StartsWith(ACEPrefix, StringComparison.OrdinalIgnoreCase);
		}

		// Based on RFC 952
		private static bool IsValidDnsNameLabel(string label) =>
			label.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-');

		// Based on RFC 6125 (without support for partial wildcard DNS labels
		// since it has been deprecated by most major browsers)
		private static bool IsValidCertificateNameFirstLabel(string label) =>
			label == "*" || IsValidDnsNameLabel(label);

		private static bool IsWildcardCertificateName(this string certName) {
			if (!certName.StartsWith("*.", StringComparison.Ordinal))
				return false;

			// the certificate name starts with a wildcard DNS label. to verify if it's a valid wildcard name,
			// we replace the wildcard by the letter 'a', then match it against the original certificate name
			return MatchesName(certName, CertificateNameType.DnsName, 'a' + certName[1..]);
		}

		private static bool MatchesName(string certName, string certNameType, string name) {
			const string Wildcard = "*";
			const char Delimiter = '.';

			if (string.IsNullOrEmpty(certName) ||
				string.IsNullOrEmpty(name))
				return false;

			// if at least one of the names is an IP address, do an exact match
			if (certNameType == CertificateNameType.IpAddress ||
				IPAddress.TryParse(certName, out _) ||
				IPAddress.TryParse(name, out _))
				return name.EqualsOrdinalIgnoreCase(certName);

			Debug.Assert(certNameType == CertificateNameType.DnsName);

			var certNameLabels = certName.Split(Delimiter);
			var dnsNameLabels = name.Split(Delimiter);

			if (certNameLabels.Length != dnsNameLabels.Length)
				return false;

			if (certNameLabels.Any(string.IsNullOrEmpty) ||
				dnsNameLabels.Any(string.IsNullOrEmpty))
				return false;

			if (certNameLabels.Any(IsInternationalizedDomainNameLabel) ||
				dnsNameLabels.Any(IsInternationalizedDomainNameLabel)) {
				var idnMapping = new IdnMapping();
				dnsNameLabels = dnsNameLabels.Select(x => idnMapping.GetAscii(x)).ToArray();
				certNameLabels = certNameLabels.Select(x => idnMapping.GetAscii(x)).ToArray();
			}

			if (!IsValidCertificateNameFirstLabel(certNameLabels.First()) ||
				!certNameLabels.Skip(1).All(IsValidDnsNameLabel) ||
				!dnsNameLabels.All(IsValidDnsNameLabel))
				return false;

			// if first label is not a wildcard, check for an exact match
			if (certNameLabels.First() != Wildcard)
				return certNameLabels.EqualsOrdinalIgnoreCase(dnsNameLabels);

			// first label is wildcard, a wildcard FQDN should have at least 3 labels
			if (certNameLabels.Length <= 2)
				return false;

			// compare the other labels of the wildcard FQDN
			return certNameLabels.Skip(1).EqualsOrdinalIgnoreCase(dnsNameLabels.Skip(1));
		}

		public static IDisposable ConvertToCertificate2(this X509Certificate certificate, out X509Certificate2 certificate2) {
			if (certificate is X509Certificate2 c2) {
				certificate2 = c2;
				return null;
			}

			certificate2 = new X509Certificate2(certificate);
			return certificate2;
		}
	}
}
