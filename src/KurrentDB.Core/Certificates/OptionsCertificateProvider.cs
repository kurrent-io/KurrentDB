// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;
using Serilog;

namespace KurrentDB.Core.Certificates;

// (Re)Loads the certificates specified in the ClusterVNodeOptions
public class OptionsCertificateProvider : CertificateProvider {
	private static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNode>();
	private string _cachedReservedNodeCN;

	public override LoadCertificateResult LoadCertificates(ClusterVNodeOptions options) {
		if (options.Application.Insecure) {
			Log.Information("Skipping reload of certificates since TLS is disabled.");
			return LoadCertificateResult.Skipped;
		}

		// Load both Node and NodeClient certificates up-front.
		// Node cert is the server cert sent to incoming connections
		// NodeClient cert is the client cert sent on outgoing connections to other nodes.
		// NodeClient cert defaults to the same certificate as Node cert if not provided.
		var (certificate, intermediates) = options.LoadNodeCertificate();
		var hasNodeClientCert = options.TryLoadNodeClientCertificate(out var nodeClientCertificate, out var nodeClientIntermediates);
		if (!hasNodeClientCert) {
			nodeClientCertificate = certificate;
			nodeClientIntermediates = intermediates;
		}

		string reservedNodeCN;
		var reservedNodeCNOption = nameof(options.Certificate.CertificateReservedNodeCommonName);

		// Determine the CN pattern expected from incoming node certificates.
		if (options.Certificate.CertificateReservedNodeCommonName.IsNotEmptyString()) {
			// Reserved CN is configured. Check that the node client cert matches it.
			reservedNodeCN = options.Certificate.CertificateReservedNodeCommonName;
			if (!nodeClientCertificate.ClientCertificateMatchesName(reservedNodeCN)) {
				var nodeClientCertCN = nodeClientCertificate.GetCommonName();
				Log.Error(
					"Certificate CN: {certificateCN} does not match with the {reservedNodeCNOption} configuration setting: {reservedNodeCN}",
					nodeClientCertCN, reservedNodeCNOption, reservedNodeCN);
				return LoadCertificateResult.VerificationFailed;
			}
			Log.Information("{reservedNodeCNOption} configured to: {reservedNodeCN}",
				reservedNodeCNOption, reservedNodeCN);
		} else {
			reservedNodeCN = nodeClientCertificate.GetCommonName();
			Log.Information("{reservedNodeCNOption} auto-configured to: {reservedNodeCN} based on certificate",
				reservedNodeCNOption, reservedNodeCN);
		}

		// Log information about the certificates and their intermediates
		LogThumbprints("node", Certificate, certificate, intermediates);
		LogThumbprints("node client", NodeClientCertificate, nodeClientCertificate, nodeClientIntermediates);

		static void LogThumbprints(string label, X509Certificate2 previousCertificate, X509Certificate2 certificate, X509Certificate2Collection intermediates) {
			var previousThumbprint = previousCertificate?.Thumbprint;
			var newThumbprint = certificate.Thumbprint;
			Log.Information("Loading the {label} certificate. Subject: {subject}, Previous thumbprint: {previousThumbprint}, New thumbprint: {newThumbprint}",
				label, certificate.SubjectName.Name, previousThumbprint, newThumbprint);

			if (intermediates != null) {
				foreach (var intermediateCert in intermediates) {
					Log.Information("Loading {label} intermediate certificate. Subject: {subject}, Thumbprint: {thumbprint}",
						label, intermediateCert.SubjectName.Name, intermediateCert.Thumbprint);
				}
			}
		}

		// Validate the node certificate
		var trustedRootCerts = options.LoadTrustedRootCertificates();

		foreach (var trustedRootCert in trustedRootCerts) {
			Log.Information("Loading trusted root for node certificate. Subject: {subject}, Thumbprint: {thumbprint}", trustedRootCert.SubjectName.Name, trustedRootCert.Thumbprint);
		}

		if (!VerifyCertificates("node", certificate, intermediates, trustedRootCerts)) {
			return LoadCertificateResult.VerificationFailed;
		}

		// Validate the node client certificate
		var nodeClientTrustedRootCerts = hasNodeClientCert
			? options.LoadNodeClientTrustedRootCertificates()
			: trustedRootCerts;

		if (hasNodeClientCert) {
			foreach (var trustedRootCert in nodeClientTrustedRootCerts) {
				Log.Information("Loading trusted root for node client certificate. Subject: {subject}, Thumbprint: {thumbprint}", trustedRootCert.SubjectName.Name, trustedRootCert.Thumbprint);
			}

			if (!VerifyCertificates("node client", nodeClientCertificate, nodeClientIntermediates, nodeClientTrustedRootCerts)) {
				return LoadCertificateResult.VerificationFailed;
			}
		}

		// Check our client certificate will be classified correctly by other nodes
		if (options.Cluster.ClusterSize > 1 && nodeClientCertificate.ClassifyInboundCertificate(
				disableClientAuthEkuValidation: options.Certificate.DisableClientAuthEkuValidation,
				out var nodeClientCertDescription) is not CertificateClassification.Node) {

			Log.Error(hasNodeClientCert
				? "The node client certificate was not recognized as a node certificate: {description}"
				: "The node certificate was not recognized as a node certificate: {description}",
				nodeClientCertDescription);
			return LoadCertificateResult.VerificationFailed;
		}
	
		// no need for a lock here since reference assignment is atomic. however, other threads may not immediately
		// see the changes and the order in which they see the changes is also not guaranteed as we don't have any
		// memory barriers here. this is not a problem as in the worst case, it will cause the certificate verifications
		// to fail when establishing/receiving a connection and the next connection retries will succeed.
		Certificate = certificate;
		IntermediateCerts = intermediates;
		NodeClientCertificate = nodeClientCertificate;
		NodeClientIntermediateCerts = nodeClientIntermediates;
		TrustedRootCerts = trustedRootCerts;
		NodeClientTrustedRootCerts = nodeClientTrustedRootCerts;
		_cachedReservedNodeCN = reservedNodeCN;

		Log.Information("All certificates successfully loaded.");
		return LoadCertificateResult.Success;
	}

	public override string GetReservedNodeCommonName() {
		return _cachedReservedNodeCN ?? throw new InvalidOperationException("Certificates are not loaded.");
	}

	private static bool VerifyCertificates(
		string label,
		X509Certificate2 certificate,
		X509Certificate2Collection intermediates,
		X509Certificate2Collection trustedRoots) {

		bool error = false;

		if (!CertificateUtils.IsValidNodeCertificate(certificate, out var errorMsg)) {
			Log.Error("The {label} certificate: {error}", label, errorMsg);
			error = true;
		}

		if (intermediates != null) {
			foreach (var cert in intermediates) {
				if (!CertificateUtils.IsValidIntermediateCertificate(cert, out errorMsg)) {
					Log.Error("{error} Please bundle only intermediate certificates (if any) and not root certificates with the {label} certificate.", errorMsg, label);
					error = true;
				}
			}
		}

		if (trustedRoots != null && trustedRoots.Count > 0) {
			foreach (var cert in trustedRoots) {
				if (!CertificateUtils.IsValidRootCertificate(cert, out errorMsg)) {
					Log.Error("{error} If you have intermediate certificates, please bundle them with the {label} certificate (in PEM or PKCS #12 format).", errorMsg, label);
					error = true;
				}
			}
		} else {
			Log.Error("No trusted root certificates loaded for the {label} certificate", label);
			error = true;
		}

		if (error)
			return false;

		var chainStatus = CertificateUtils.BuildChain(certificate, intermediates, trustedRoots, out var chainStatusInformation);

		if (chainStatus != X509ChainStatusFlags.NoError) {
			Log.Error(
				"Failed to build the certificate chain with the {label} certificate up to the root. " +
				"If you have intermediate certificates, please bundle them with the {label} certificate (in PEM or PKCS #12 format). Errors:-",
				label, label);
			foreach (var status in chainStatusInformation) {
				Log.Error(status);
			}

			error = true;
		}

		if (!error && intermediates != null) {
			chainStatus = CertificateUtils.BuildChain(certificate, null, trustedRoots, out chainStatusInformation);

			// Adding the intermediate certificates to the store is required so that
			// i)  the full certificate chain (excluding the root) is sent from client to server (on both Windows/Linux)
			//     and from server to client (on Windows only) during the TLS connection establishment
			// ii) to prevent AIA certificate downloads
			//
			// see: https://github.com/dotnet/runtime/issues/47680#issuecomment-771093045
			// and https://github.com/dotnet/runtime/issues/59979

			if (chainStatus != X509ChainStatusFlags.NoError) {
				Log.Warning(
					"For correct functioning and optimal performance, please add your intermediate certificates to the current user's " +
						(RuntimeInformation.IsWindows ?
						"'Intermediate Certification Authorities' certificate store." :
						"'CertificateAuthority' certificate store using the dotnet-certificate-tool.")
				);
			}
		}

		if (!error) {
			Log.Information("The {label} certificate chain verification successful.", label);
		}

		return !error;
	}
}
