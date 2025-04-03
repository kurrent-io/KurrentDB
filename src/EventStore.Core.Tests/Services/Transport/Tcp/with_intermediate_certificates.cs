// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Certificates;
using EventStore.Transport.Tcp;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class with_intermediate_certificates : with_certificate_chain_of_length_3 {
	private TcpServerListener _listener;
	private IPEndPoint _serverEndPoint;
	private ITcpConnection _client;
	private Func<X509Certificate, X509Chain, SslPolicyErrors, (bool, string)> _clientCertValidator;
	private X509Certificate2 _cert;

	[SetUp]
	public void SetUp() {
		// certificate exported to PKCS #12 due to this issue on Windows: https://github.com/dotnet/runtime/issues/45680
		_cert = new X509Certificate2(_leaf.ExportToPkcs12());

		_clientCertValidator = (_,_,_) => (true, null);
		_serverEndPoint = new IPEndPoint(IPAddress.Loopback, PortsHelper.GetAvailablePort(IPAddress.Loopback));
		_listener = new TcpServerListener(_serverEndPoint);
		_listener.StartListening((endPoint, socket) => {
			TcpConnectionSsl.CreateServerFromSocket(
				Guid.NewGuid(),
				endPoint,
				socket,
				() => _cert,
				() => new X509Certificate2Collection(_intermediate),
				(cert,chain,errors) => _clientCertValidator(cert, chain, errors),
				verbose: true);
		}, "Secure");
	}

	[Test]
	public void server_should_send_intermediate_certificate_during_handshake() {
		var done = new ManualResetEventSlim(false);

		bool gotLeaf = false;
		bool gotIntermediate = false;

		_client = TcpConnectionSsl.CreateConnectingConnection(
			Guid.NewGuid(),
			_serverEndPoint.GetHost(),
			null,
			_serverEndPoint,
			(certificate, chain, _, _) => {
				gotLeaf = _leaf.Equals(certificate);
				foreach (var chainElement in chain.ChainElements) {
					if (chainElement.Certificate.Equals(_intermediate)) {
						gotIntermediate = true;
					}
				}

				done.Set();
				return (true, null);
			},
			null,
			new TcpClientConnector(),
			TcpConnectionManager.ConnectionTimeout,
			conn => { },
			(conn, err) => { },
			verbose: true);

		Assert.True(done.Wait(20000), "Took too long to receive completion.");
		Assert.True(gotLeaf);
		Assert.True(gotIntermediate);
	}

	[Test, Ignore("Skipped since it adds an intermediate certificate to the current user's store")]
	public void client_should_send_intermediate_certificate_during_handshake() {
		try {
			// see: https://github.com/dotnet/runtime/issues/47680#issuecomment-771093045
			AddIntermediateCertificateToStore();

			var done = new ManualResetEventSlim(false);

			bool gotLeaf = false;
			bool gotIntermediate = false;

			_clientCertValidator = (certificate, chain, _) => {
				gotLeaf = _leaf.Equals(certificate);
				foreach (var chainElement in chain.ChainElements) {
					if (chainElement.Certificate.Equals(_intermediate)) {
						gotIntermediate = true;
					}
				}

				done.Set();
				return (true, null);
			};

			_client = TcpConnectionSsl.CreateConnectingConnection(
				Guid.NewGuid(),
				_serverEndPoint.GetHost(),
				null,
				_serverEndPoint,
				(_, _, _, _) => (true, null),
				() => new X509Certificate2Collection(_cert),
				new TcpClientConnector(),
				TcpConnectionManager.ConnectionTimeout,
				conn => { },
				(conn, err) => { },
				verbose: true);

			Assert.True(done.Wait(20000), "Took too long to receive completion.");
			Assert.True(gotLeaf);
			Assert.True(gotIntermediate);
		} finally {
			RemoveIntermediateCertificateFromStore();
		}
	}

	private void AddIntermediateCertificateToStore() {
		using var intermediateStore = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
		intermediateStore.Open(OpenFlags.ReadWrite);
		intermediateStore.Add(_intermediate);
	}

	private void RemoveIntermediateCertificateFromStore() {
		using var intermediateStore = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
		intermediateStore.Open(OpenFlags.ReadWrite);
		intermediateStore.Remove(_intermediate);
	}

	[TearDown]
	public void TearDown() {
		_listener.Stop();
		_client.Close("Normal close.");
	}
}
