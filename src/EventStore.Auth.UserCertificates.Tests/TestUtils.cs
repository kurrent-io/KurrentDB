// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EventStore.Auth.UserCertificates.Tests;

public static class TestUtils {
	private static readonly Random Random = new();
	private static string GetCharset() {
		var charset = "";
		for (var c = 'a'; c <= 'z'; c++) {
			charset += c;
		}
		for (var c = 'A'; c <= 'Z'; c++) {
			charset += c;
		}
		for (var c = '0'; c <= '9'; c++) {
			charset += c;
		}
		return charset;
	}

	public static string GenerateSubject() {
		var charset = GetCharset();
		string s = "";
		for (var j = 0; j < 10; j++) {
			s += charset[Random.Next() % charset.Length];
		}

		return $"CN={s}";
	}

	private static byte[] GenerateSerialNumber() {
		var charset = GetCharset();
		string s = "";
		for (var j = 0; j < 10; j++) {
			s += charset[Random.Next() % charset.Length];
		}

		var utf8Encoding = new UTF8Encoding(false);
		return utf8Encoding.GetBytes(s);
	}

	public static X509Certificate2 CreateCertificate(
		bool ca,
		RSA keyPair,
		string subject,
		(RSA keyPair, X500DistinguishedName subjectName)? parentInfo,
		bool expired,
		bool unready,
		bool serverAuthEKU,
		bool clientAuthEKU) {

		var certReq = new CertificateRequest(subject, keyPair, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

		var now = DateTimeOffset.UtcNow;
		now = new DateTime(now.Year, now.Month, now.Day); // round to nearest day
		var startDate = !unready ? now.AddMonths(-1) : now.AddDays(1);
		var endDate = !expired ? now.AddMonths(1) : now.AddDays(-1);

		if (ca) {
			certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(
				certificateAuthority: true,
				hasPathLengthConstraint: false,
				pathLengthConstraint: 0,
				critical: true));
		}

		if (serverAuthEKU || clientAuthEKU) {
			certReq.CertificateExtensions.Add(new X509KeyUsageExtension(
				keyUsages: X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
				critical: false));

			var oids = new OidCollection();

			if (serverAuthEKU)
				oids.Add(new Oid("1.3.6.1.5.5.7.3.1"));

			if (clientAuthEKU)
				oids.Add(new Oid("1.3.6.1.5.5.7.3.2"));

			certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
		}

		if (parentInfo == null) {
			return certReq.CreateSelfSigned(startDate, endDate);
		}

		var parentKey = parentInfo.Value.keyPair;
		var signatureGenerator = X509SignatureGenerator.CreateForRSA(parentKey!, RSASignaturePadding.Pkcs1);
		return certReq.Create(parentInfo.Value.subjectName, signatureGenerator, startDate, endDate, GenerateSerialNumber()).CopyWithPrivateKey(keyPair);
	}

	public static X509Certificate2 CreateCertificate(
		bool ca,
		RSA keyPair,
		string subject,
		(RSA keyPair, X500DistinguishedName subjectName)? parentInfo,
		bool expired,
		bool serverAuthEKU = false,
		bool clientAuthEKU = false) =>

		CreateCertificate(
			ca, keyPair, subject, parentInfo, expired,
			unready: false, serverAuthEKU: serverAuthEKU, clientAuthEKU: clientAuthEKU);

	public static X509Certificate2 CreateCertificate(
		bool ca = false,
		X509Certificate2? parent = null,
		bool expired = false,
		string? subject = null,
		RSA? keyPair = null,
		bool serverAuthEKU = false,
		bool clientAuthEKU = false) {

		using var rsa = RSA.Create();
		subject ??= GenerateSubject();

		(RSA, X500DistinguishedName)? parentInfo = null;
		if (parent != null) {
			parentInfo = (parent.GetRSAPrivateKey()!, parent.SubjectName);
		}

		return CreateCertificate(ca, keyPair ?? rsa, subject, parentInfo, expired,
			serverAuthEKU: serverAuthEKU,
			clientAuthEKU: clientAuthEKU);
	}
}
