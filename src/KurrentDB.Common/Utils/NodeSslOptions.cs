// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace KurrentDB.Common.Utils;

/// <summary>
/// TLS options for talking to other nodes in the cluster, whoever is doing the talking: the node's
/// gRPC clients build a <see cref="System.Net.Http.SocketsHttpHandler"/> around the client options,
/// and the Kontrol Plane's Raft transport takes both halves directly.
/// </summary>
public static class NodeSslOptions {
	/// <summary>
	/// Options for connecting to another node, for transports that set the target host themselves.
	/// </summary>
	/// <param name="additionalCertificateNames">
	/// Names the peer's certificate may carry in addition to the target host, from
	/// <see cref="EndpointExtensions.GetOtherNames"/>. Only DNS-discovered endpoints have any, so
	/// null is the usual value for statically configured peers.
	/// </param>
	/// <param name="enabledSslProtocols">
	/// <see cref="NodeTlsPolicy.PinnedSslProtocols"/> or
	/// <see cref="NodeTlsPolicy.SystemSslProtocols"/>. Explicit because the two are a real
	/// choice: pinning excludes a protocol the system would allow, and offers one it has disabled.
	/// </param>
	public static SslClientAuthenticationOptions CreateUntargetedClientOptions(
		CertificateDelegates.ServerCertificateValidator serverCertificateValidator,
		Func<X509Certificate> clientCertificateSelector,
		string[] additionalCertificateNames,
		SslProtocols enabledSslProtocols) => new() {

		EnabledSslProtocols = enabledSslProtocols,
		CertificateRevocationCheckMode = NodeTlsPolicy.CertificateRevocationCheckMode,
		RemoteCertificateValidationCallback = NodeTlsPolicy.ForServerCertificate(
			serverCertificateValidator,
			() => additionalCertificateNames),
		LocalCertificateSelectionCallback = (_, _, _, _, _) => clientCertificateSelector(),
	};

	/// <summary>
	/// Options for connecting to one particular node, whose certificate is expected to name it.
	/// </summary>
	/// <param name="target">The address being dialled. Its host is the name the peer's certificate
	/// has to carry, and its other names are the ones it may carry instead.</param>
	public static SslClientAuthenticationOptions CreateTargetedClientOptions(
		EndPoint target,
		CertificateDelegates.ServerCertificateValidator serverCertificateValidator,
		Func<X509Certificate> clientCertificateSelector,
		SslProtocols enabledSslProtocols) {

		var options = CreateUntargetedClientOptions(
			serverCertificateValidator: serverCertificateValidator,
			clientCertificateSelector: clientCertificateSelector,
			additionalCertificateNames: target.GetOtherNames(),
			enabledSslProtocols: enabledSslProtocols);

		options.TargetHost = target.GetHost();
		return options;
	}

	/// <summary>
	/// Options for accepting a connection from another node: present this node's certificate, and
	/// require and validate the certificate the peer presents.
	/// </summary>
	public static SslServerAuthenticationOptions CreateServerOptions(
		CertificateDelegates.ClientCertificateValidator clientCertificateValidator,
		Func<X509Certificate> serverCertificateSelector) => new() {

		ClientCertificateRequired = true,
		EnabledSslProtocols = NodeTlsPolicy.PinnedSslProtocols,
		CertificateRevocationCheckMode = NodeTlsPolicy.CertificateRevocationCheckMode,
		AllowRenegotiation = NodeTlsPolicy.AllowRenegotiation,
		ServerCertificateSelectionCallback = (_, _) => serverCertificateSelector(),
		RemoteCertificateValidationCallback = NodeTlsPolicy.ForClientCertificate(clientCertificateValidator),
	};
}
