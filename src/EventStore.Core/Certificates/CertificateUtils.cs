// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using System.Text;
using EventStore.Core.Exceptions;

namespace EventStore.Core;

public static class CertificateUtils {
	public static X509Certificate2 LoadFromStore(StoreLocation storeLocation, StoreName storeName,
		string certificateSubjectName, string certificateThumbprint) {
		var store = new X509Store(storeName, storeLocation);
		return GetCertificateFromStore(store, certificateSubjectName, certificateThumbprint);
	}

	public static X509Certificate2 LoadFromStore(StoreName storeName, string certificateSubjectName,
		string certificateThumbprint) {
		var store = new X509Store(storeName);
		return GetCertificateFromStore(store, certificateSubjectName, certificateThumbprint);
	}

	private static bool TryReadPkcs12CertificateBundle
		(string certificatePath,
		string password,
		out X509Certificate2 certificate,
		out X509Certificate2Collection intermediates) {
		var pkcs12Info =  Pkcs12Info.Decode(File.ReadAllBytes(certificatePath), out _);

		var certs = new X509Certificate2Collection();
		using var rsa = RSA.Create();
		var privateKeyFound = false;

		foreach (var safeContents in pkcs12Info.AuthenticatedSafe) {
			if (safeContents.ConfidentialityMode == Pkcs12ConfidentialityMode.Password) {
				safeContents.Decrypt(password);
			}

			foreach (var bag in safeContents.GetBags()) {
				switch (bag)
				{
					case Pkcs12CertBag certBag:
						certs.Add(certBag.GetCertificate());
						break;
					case Pkcs12ShroudedKeyBag shroudedKeyBag:
					{
						if (privateKeyFound) {
							throw new Exception("Multiple private keys found");
						}
						var encryptedKey = shroudedKeyBag.EncryptedPkcs8PrivateKey;
						rsa.ImportEncryptedPkcs8PrivateKey(password.AsSpan(), encryptedKey.Span, out _);
						privateKeyFound = true;
						break;
					}
				}
			}
		}

		if (certs.Count == 0) {
			throw new Exception("No certificates found");
		}

		if (!privateKeyFound) {
			throw new Exception("No private keys found");
		}

		using var publicCertificate = certs[0];
		using var publicWithPrivate = publicCertificate.CopyWithPrivateKey(rsa);
		certs.RemoveAt(0);
		certificate = new X509Certificate2(publicWithPrivate.ExportToPkcs12());
		intermediates = certs.Count == 0 ? null : certs;
		return true;
	}

	public static (X509Certificate2 certificate, X509Certificate2Collection intermediates) LoadFromFile(
		string certificatePath,
		string privateKeyPath,
		string certificatePassword, string certificatePrivateKeyPassword = null) {

		if (string.IsNullOrEmpty(privateKeyPath)) {
			// no private key file - assume PKCS12 format
			// the check is done on the private key file since an empty password may still be valid

			try {
				if (TryReadPkcs12CertificateBundle(
					certificatePath,
					certificatePassword,
					out var nodeCertificate,
					out var intermediateCertificates)) {
					return (nodeCertificate, intermediateCertificates);
				}
			} catch(CryptographicException) {
				// ignored
			}

			// if the above attempt failed, try to load the certificate via the standard constructor
			var certificate = new X509Certificate2(certificatePath, certificatePassword);
			if (!certificate.HasPrivateKey)
				throw new NoCertificatePrivateKeyException();
			return (certificate, null);
		}

		string[] allLines = File.ReadAllLines(privateKeyPath);
		var header = allLines[0].Replace("-", "");
		var privateKey = Convert.FromBase64String(string.Join(string.Empty, allLines.Skip(1).SkipLast(1)));

		using var rsa = RSA.Create();
		switch (header) {
			case "BEGIN PRIVATE KEY":
				rsa.ImportPkcs8PrivateKey(new ReadOnlySpan<byte>(privateKey), out _);
				break;
			case "BEGIN RSA PRIVATE KEY":
				rsa.ImportRSAPrivateKey(new ReadOnlySpan<byte>(privateKey), out _);
				break;
			case "BEGIN ENCRYPTED PRIVATE KEY":
				if (certificatePrivateKeyPassword is null) {
					throw new ArgumentException(
						"A password is required to read the certificate's private key file.");
				}
				try {
					rsa.ImportEncryptedPkcs8PrivateKey(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(certificatePrivateKeyPassword)), new ReadOnlySpan<byte>(privateKey), out _);
				} catch (CryptographicException e) {
					throw new ArgumentException(
						"Failed to read the certificate's private key file. The password may be incorrect or the private key may not be an RSA key.", e);
				}
				break;
			default:
				throw new NotSupportedException($"Unsupported private key file format: {header}");
				
		}
		

		// assume it's a PEM bundle
		var certificateBundle = new X509Certificate2Collection();
		var bundleLoadSucceeded = false;
		try {
			certificateBundle.ImportFromPemFile(certificatePath);
			bundleLoadSucceeded = true;
		} catch {
			// ignored
		}

		if (!bundleLoadSucceeded || certificateBundle.Count == 0) {
			// make a last attempt to open the certificate, leaving the file format guess-work to the library
			certificateBundle.Add(new X509Certificate2(certificatePath, certificatePassword));
		}

		using var publicCertificate = certificateBundle[0];
		using var publicWithPrivate = publicCertificate.CopyWithPrivateKey(rsa);
		var serverCertificate = new X509Certificate2(publicWithPrivate.ExportToPkcs12());
		certificateBundle.RemoveAt(0);
		var intermediates = certificateBundle.Count == 0 ? null : certificateBundle;

		return (serverCertificate, intermediates);
	}

	private static X509Certificate2 GetCertificateFromStore(X509Store store, string certificateSubjectName,
		string certificateThumbprint) {
		try {
			store.Open(OpenFlags.OpenExistingOnly);
		} catch (Exception exc) {
			throw new Exception(
				string.Format("Could not open certificate store '{0}' in location {1}'.", store.Name,
					store.Location), exc);
		}

		if (!string.IsNullOrWhiteSpace(certificateThumbprint)) {
			var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, certificateThumbprint, true);
			if (certificates.Count == 0)
				throw new Exception($"Could not find valid certificate with thumbprint '{certificateThumbprint}'.");

			if (certificates.Count > 1)
				throw new Exception(
					$"Could not determine a unique certificate from thumbprint '{certificateThumbprint}'.");

			return certificates[0];
		}

		if (!string.IsNullOrWhiteSpace(certificateSubjectName)) {
			var certificates =
				store.Certificates.Find(X509FindType.FindBySubjectName, certificateSubjectName, true);
			if (certificates.Count == 0)
				throw new Exception(
					$"Could not find valid certificate with subject name '{certificateSubjectName}'.");

			if (certificates.Count == 1) {
				return certificates[0];
			}

			// If we get multiple of the same certificate, pick the one which expires last to allow rolling certificates
			var certs = certificates.GetEnumerator();
			X509Certificate2 mostRecentCert = null;
			while (certs.MoveNext()) {
				if (certs.Current is null) {
					continue;
				}

				if (mostRecentCert is null) {
					mostRecentCert = certs.Current;
				} else if (certs.Current.NotAfter > mostRecentCert.NotAfter) {
					mostRecentCert = certs.Current;
				}
			}

			return mostRecentCert;
		}

		throw new ArgumentException(
			"No thumbprint or subject name was specified for a certificate, but a certificate store was specified.");
	}

	public static IEnumerable<(string fileName,X509Certificate2 certificate)> LoadAllCertificates(string path) {
		var files = Directory.GetFiles(path);
		var acceptedExtensions = new[] {".crt", ".cert", ".cer", ".pem", ".der"};
		foreach (var file in files) {
			var fileInfo = new FileInfo(file);
			var extension = fileInfo.Extension;
			if (acceptedExtensions.Contains(extension)) {
				X509Certificate2 cert;
				try {
					cert = new X509Certificate2(File.ReadAllBytes(file));
				} catch (Exception exc) {
					throw new AggregateException($"Error loading certificate file: {file}", exc);
				}
				yield return (file, cert);
			}
		}
	}

	public static StoreLocation GetCertificateStoreLocation(string certificateStoreLocation) {
		if (!Enum.TryParse(certificateStoreLocation, out StoreLocation location))
			throw new Exception($"Could not find certificate store location '{certificateStoreLocation}'");
		return location;
	}

	public static StoreName GetCertificateStoreName(string certificateStoreName) {
		if (!Enum.TryParse(certificateStoreName, out StoreName name))
			throw new Exception($"Could not find certificate store name '{certificateStoreName}'");
		return name;
	}

	public static X509ChainStatusFlags BuildChain(X509Certificate certificate,
		X509Certificate2Collection intermediateCerts, X509Certificate2Collection trustedRootCerts,
		out List<string> statusInformation) {
		using var chain = new X509Chain {
			ChainPolicy = {
				RevocationMode = X509RevocationMode.NoCheck,
				TrustMode = X509ChainTrustMode.CustomRootTrust,
				DisableCertificateDownloads = true
			}
		};

		if (trustedRootCerts != null) {
			foreach (var cert in trustedRootCerts) {
				chain.ChainPolicy.CustomTrustStore.Add(cert);
			}
		}

		if (intermediateCerts != null) {
			foreach (var cert in intermediateCerts) {
				chain.ChainPolicy.ExtraStore.Add(cert);
			}
		}

		chain.Build(new X509Certificate2(certificate));

		var chainStatus = X509ChainStatusFlags.NoError;
		statusInformation = new List<string>();

		var cn = ((X509Certificate2)certificate).GetCommonName();

		foreach (var status in chain.ChainStatus) {
			chainStatus |= status.Status;
			statusInformation.Add($"{cn} : {status.StatusInformation}");
		}

		return chainStatus;
	}

	public static bool IsValidNodeCertificate(X509Certificate2 certificate, out string error) {
		if (certificate.HasPrivateKey) {
			error = null;
			return true;
		}

		error = $"Node's certificate with thumbprint '{certificate.Thumbprint}' has no associated private key.";
		return false;
	}

	public static bool IsValidIntermediateCertificate(X509Certificate2 certificate, out string error) {
		if (certificate.SubjectName.Name != certificate.IssuerName.Name) {
			error = null;
			return true;
		}

		error = $"Certificate with thumbprint '{certificate.Thumbprint}' does not appear to be a valid intermediate certificate " +
		        $"since it is self-signed (subject = {certificate.SubjectName.Name}, issuer = {certificate.IssuerName.Name}).";
		return false;
	}

	public static bool IsValidRootCertificate(X509Certificate2 certificate, out string error) {
		if (certificate.SubjectName.Name == certificate.IssuerName.Name) {
			error = null;
			return true;
		}

		error =
			$"Certificate with thumbprint '{certificate.Thumbprint}' does not appear to be a valid root certificate " +
			$"since it is not self-signed (subject = {certificate.SubjectName.Name}, issuer = {certificate.IssuerName.Name}).";
		return false;
	}
}
