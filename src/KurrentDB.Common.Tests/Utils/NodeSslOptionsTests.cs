// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;

namespace KurrentDB.Common.Tests.Utils;

public class NodeSslOptionsTests {
	[Fact]
	public void targeted_options_name_the_peer_being_dialled() {
		var options = NodeSslOptions.CreateTargetedClientOptions(
			new DnsEndPoint("node1.example.com", 3111),
			serverCertificateValidator: Accept,
			clientCertificateSelector: NoCertificate,
			enabledSslProtocols: NodeTlsPolicy.PinnedSslProtocols);

		Assert.Equal("node1.example.com", options.TargetHost);
	}

	[Fact]
	public void a_dns_discovered_peer_is_dialled_by_ip_and_may_present_the_cluster_name() {
		string[] otherNames = [];
		var options = NodeSslOptions.CreateTargetedClientOptions(
			new IPWithClusterDnsEndPoint(IPAddress.Loopback, "cluster.example.com", 3111),
			serverCertificateValidator: (_, _, _, names) => {
				otherNames = names;
				return (true, null!);
			},
			clientCertificateSelector: NoCertificate,
			enabledSslProtocols: NodeTlsPolicy.PinnedSslProtocols);

		// The address we dial is the resolved one, but the certificate names the cluster
		Assert.Equal("127.0.0.1", options.TargetHost);

		options.RemoteCertificateValidationCallback!(this, null, null, SslPolicyErrors.None);
		Assert.Equal(["cluster.example.com"], otherNames);
	}

	// SocketsHttpHandler clones these per host and fills in the target host itself, so there is nothing
	// here to name the peer. Anything else that dialled with these would not check the name at all.
	[Fact]
	public void untargeted_options_name_no_peer() {
		var options = NodeSslOptions.CreateUntargetedClientOptions(
			serverCertificateValidator: Accept,
			clientCertificateSelector: NoCertificate,
			additionalCertificateNames: null,
			enabledSslProtocols: NodeTlsPolicy.SystemSslProtocols);

		Assert.Null(options.TargetHost);
	}

	[Theory]
	[InlineData(SslProtocols.None)]
	[InlineData(SslProtocols.Tls12 | SslProtocols.Tls13)]
	public void the_enabled_protocols_are_the_ones_asked_for(SslProtocols enabledSslProtocols) {
		var options = NodeSslOptions.CreateTargetedClientOptions(
			new DnsEndPoint("node1.example.com", 3111),
			serverCertificateValidator: Accept,
			clientCertificateSelector: NoCertificate,
			enabledSslProtocols: enabledSslProtocols);

		Assert.Equal(enabledSslProtocols, options.EnabledSslProtocols);
	}

	private static (bool, string) Accept(
		X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors, string[] otherNames)
		=> (true, null!);

	private static X509Certificate NoCertificate() => null!;
}
