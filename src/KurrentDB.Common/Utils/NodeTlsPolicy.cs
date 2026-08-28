// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace KurrentDB.Common.Utils;

/// <summary>
/// The TLS settings this node applies to connections it makes to, or accepts from, other nodes in
/// the cluster, whichever transport carries them: the internal TCP replication channel, the node's
/// gRPC clients, and the Kontrol Plane's Raft transport.
/// </summary>
/// <remarks>
/// Here rather than in each handshake so that the paths cannot drift apart - in particular so that
/// no one path can quietly end up accepting an older protocol than the others, or validating a peer
/// certificate on different terms. What legitimately differs between handshakes - which protocols
/// are advertised, whether a peer certificate is required - is still chosen per handshake, but from
/// the values named here.
/// </remarks>
public static class NodeTlsPolicy {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(NodeTlsPolicy));

	/// <summary>
	/// The protocols to offer where we control both ends of the connection and can require them.
	/// </summary>
	public const SslProtocols PinnedSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

	/// <summary>
	/// Defers the choice to the machine's own crypto policy, so that an operator disabling a protocol
	/// system-wide is obeyed and a newer one is picked up without a code change. The right choice for
	/// listeners and clients that other people's software connects to or through.
	/// </summary>
	public const SslProtocols SystemSslProtocols = SslProtocols.None;

	/// <summary>
	/// Node certificates are validated against the configured intermediates and trusted roots.
	/// Revocation lists are not consulted, since nodes are not expected to be able to reach them.
	/// </summary>
	public const X509RevocationMode CertificateRevocationCheckMode = X509RevocationMode.NoCheck;

	/// <summary>
	/// The same policy as <see cref="CertificateRevocationCheckMode"/>, for the authentication
	/// overloads that take a flag rather than a mode.
	/// </summary>
	public const bool CheckCertificateRevocation = false;

	public const bool AllowRenegotiation = false;

	/// <summary>
	/// Validates the certificate presented by the node we have connected to.
	/// </summary>
	/// <param name="additionalCertificateNames">
	/// Names the peer's certificate may carry besides the address we dialled. Resolved per callback,
	/// because callers such as HttpSendService follow leadership as it moves.
	/// </param>
	public static RemoteCertificateValidationCallback ForServerCertificate(
		CertificateDelegates.ServerCertificateValidator validator,
		Func<string[]> additionalCertificateNames) =>

		(_, certificate, chain, sslPolicyErrors) => {
			var (isValid, error) = validator(certificate, chain, sslPolicyErrors, additionalCertificateNames());
			if (!isValid && error != null) {
				Log.Error("Server certificate validation error: {e}", error);
			}

			return isValid;
		};

	/// <summary>
	/// Validates the certificate presented by whoever has connected to us.
	/// </summary>
	/// <param name="allowNoCertificate">
	/// True where the listener is also reached by ordinary clients, which are not expected to present
	/// one. False between nodes, where a missing certificate is a failed handshake.
	/// </param>
	public static RemoteCertificateValidationCallback ForClientCertificate(
		CertificateDelegates.ClientCertificateValidator validator,
		bool allowNoCertificate) =>

		(_, certificate, chain, sslPolicyErrors) => {
			if (certificate is null)
				return allowNoCertificate;

			var (isValid, error) = validator(certificate, chain, sslPolicyErrors);
			if (!isValid && error != null) {
				Log.Error("Client certificate validation error: {e}", error);
			}

			return isValid;
		};
}
