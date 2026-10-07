// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.DevCertificates;
using KurrentDB.Core;
using KurrentDB.Core.Certificates;
using ILogger = Serilog.ILogger;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace KurrentDB;

/// <summary>
/// Where a node gets the certificate it presents, which is either configured or, in dev mode, made here.
/// </summary>
public static class CertificateProviders {
	static ILogger Log => Serilog.Log.ForContext(typeof(CertificateProviders));

	/// <summary>
	/// Builds the provider the node's configuration asks for.
	/// </summary>
	/// <remarks>
	/// Dev mode writes to the filesystem and, on Windows, to the user's certificate store. That only
	/// happens when <c>DevMode.Dev</c> is set, which no host sets on anyone's behalf.
	/// </remarks>
	/// <returns>
	/// True with <paramref name="provider"/> set. Otherwise false, with <paramref name="error"/> saying
	/// why a dev certificate could not be obtained — the configured case cannot fail here, because
	/// <see cref="OptionsCertificateProvider"/> reads the certificate when the node loads it.
	/// </returns>
	public static bool TryCreate(
		ClusterVNodeOptions options,
		[NotNullWhen(true)] out CertificateProvider? provider,
		[NotNullWhen(false)] out string? error) {

		if (!options.DevMode.Dev) {
			provider = new OptionsCertificateProvider();
			error = null;
			return true;
		}

		var log = Log;
		log.Information("Dev mode is enabled.");
		log.Warning(
			"\n==============================================================================================================\n" +
			"DEV MODE IS ON. THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n" +
			"DEV MODE WILL GENERATE AND TRUST DEV CERTIFICATES FOR RUNNING A SINGLE SECURE NODE ON LOCALHOST.\n" +
			"==============================================================================================================\n");

		var manager = CertificateManager.Instance;
		var devCertPath = options.DevMode.DevCertPath;
		X509Certificate2? devCert = null;

		// If a cert path is specified, try to load an existing cert from it
		if (!string.IsNullOrEmpty(devCertPath)) {
			devCert = DevCertificateFile.TryLoad(devCertPath);
			if (devCert is not null) {
				log.Information("Dev certificate loaded from {path}", devCertPath);
			} else if (File.Exists(devCertPath)) {
				log.Warning("Dev certificate at {path} is invalid or expired, generating a new one.", devCertPath);
			}
		}

		if (devCert is null) {
			// Generate a new certificate and optionally export to file
			var result = manager.EnsureDevelopmentCertificate(
				DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
				out devCert,
				path: devCertPath,
				includePrivateKey: !string.IsNullOrEmpty(devCertPath));
			if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent)) {
				provider = null;
				error = $"Could not ensure dev certificate is available. Reason: {result}";
				return false;
			}

			if (devCert is null) {
				provider = null;
				error = "Could not create dev certificate. " +
						"If the home directory is not writable (e.g., in a container), " +
						"use --dev-cert-path to specify an alternative file location.";
				return false;
			}

			if (!string.IsNullOrEmpty(devCertPath)) {
				log.Information("Dev certificate saved to {path}", devCertPath);
			}
		}

		// Write public cert as .crt for clients to trust
		if (!string.IsNullOrEmpty(devCertPath)) {
			try {
				var crtPath = DevCertificateFile.WritePublicCertificate(devCert, devCertPath);
				log.Information("Dev certificate public key saved to {path} (use this to configure client trust)",
					crtPath);
			} catch (Exception ex) {
				log.Warning("Could not write public certificate: {error}", ex.Message);
			}
		}

		if (!manager.IsTrusted(devCert) && RuntimeInformation.IsWindows) {
			log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", devCert);
			manager.TrustCertificate(devCert);
		} else if (!RuntimeInformation.IsWindows) {
			log.Warning("Automatically trusting dev certs is only supported on Windows.\n" +
						"Please trust certificate {cert} if it's not trusted already.", devCert);
		}

		log.Information("Running in dev mode using certificate '{cert}'", devCert);
		provider = new DevCertificateProvider(devCert);
		error = null;
		return true;
	}
}
