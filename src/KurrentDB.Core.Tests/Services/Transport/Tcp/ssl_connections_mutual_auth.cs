// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Services.Transport.Tcp;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.Tests.Logging;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Transport.Tcp;
using NUnit.Framework;
using Serilog;

namespace KurrentDB.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class ssl_connections_mutual_auth {
	private static readonly ILogger Log = TestLogger.Create<ssl_connections_mutual_auth>();
	private IPAddress _ip;
	private int _port;

	[SetUp]
	public void SetUp() {
		Serilog.Log.Logger = TestLogger.Logger;
		_ip = IPAddress.Loopback;
		_port = PortsHelper.GetAvailablePort(_ip);
	}

	[TestCase(true, true, true, true, true)] //require valid server and client certificate
	[TestCase(true, false, true, true, false)] //require valid server and client certificate
	[TestCase(false, true, true, true, false)] //require valid server and client certificate
	[TestCase(false, false, true, true, false)] //require valid server and client certificate
	[TestCase(true, true, true, false, true)] //require valid server certificate only
	[TestCase(true, false, true, false, true)] //require valid server certificate only
	[TestCase(false, true, true, false, false)] //require valid server certificate only
	[TestCase(false, false, true, false, false)] //require valid server certificate only
	[TestCase(true, true, false, true, true)] //require valid client certificate only
	[TestCase(true, false, false, true, false)] //require valid client certificate only
	[TestCase(false, true, false, true, true)] //require valid client certificate only
	[TestCase(false, false, false, true, false)] //require valid client certificate only
	[TestCase(true, true, false, false, true)] //do not require valid client or server certificate
	[TestCase(true, false, false, false, true)] //do not require valid client or server certificate
	[TestCase(false, true, false, false, true)] //do not require valid client or server certificate
	[TestCase(false, false, false, false, true)] //do not require valid client or server certificate
	public void should_connect_to_each_other_and_send_data_depending_on_certificate_validity_and_settings(
		bool useValidServerCertificate,
		bool useValidClientCertificate,
		bool validateServerCertificate,
		bool validateClientCertificate,
		bool shouldConnectSuccessfully
	) {
		var serverEndPoint = new IPEndPoint(_ip, _port);
		var serverCertificate = useValidServerCertificate
			? ssl_connections.GetServerCertificate()
			: ssl_connections.GetUntrustedCertificate();
		var clientCertificate = useValidClientCertificate
			? ssl_connections.GetOtherServerCertificate()
			: ssl_connections.GetUntrustedCertificate();
		var rootCertificates = new X509Certificate2Collection(ssl_connections.GetRootCertificate());

		var sent = new byte[1000];
		new Random().NextBytes(sent);

		var received = new MemoryStream();

		var done = new ManualResetEventSlim();

		var listener = new TcpServerListener(serverEndPoint);
		listener.StartListening((endPoint, socket) => {
			var ssl = TcpConnectionSsl.CreateServerFromSocket(
				Guid.NewGuid(),
				endPoint,
				socket,
				() => serverCertificate,
				null,
				(cert, chain, err) => validateClientCertificate
					? ClusterVNode<string>.ValidateClientCertificate(cert, chain, err, () => null, () => rootCertificates)
					: (true, null),
				verbose: true);
			ssl.ConnectionClosed += (_, error) => {
				Log.Information("Connection closed: {Error}.", error);
				done.Set();
			};
			if (ssl.IsClosed)
				done.Set();

			Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback = null;
			callback = (_, y) => {
				foreach (var arraySegment in y) {
					received.Write(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
					Log.Information("Received: {0} bytes, total: {1}.", arraySegment.Count, received.Length);
				}

				if (received.Length >= sent.Length) {
					Log.Information("Done receiving...");
					done.Set();
				} else {
					Log.Information("Receiving...");
					ssl.ReceiveAsync(callback);
				}
			};
			Log.Information("Receiving...");
			ssl.ReceiveAsync(callback);
		}, "Secure");

		var clientSsl = TcpConnectionSsl.CreateConnectingConnection(
			Guid.NewGuid(),
			serverEndPoint.GetHost(),
			null,
			serverEndPoint,
			(cert, chain, err, _) => validateServerCertificate
				? ClusterVNode<string>.ValidateServerCertificate(cert, chain, err, () => null, () => rootCertificates, null)
				: (true, null),
			() => new X509CertificateCollection { clientCertificate },
			new TcpClientConnector(),
			TcpConnectionManager.ConnectionTimeout,
			conn => {
				Log.Information("Sending bytes...");
				conn.EnqueueSend([new(sent)]);
			},
			(_, err) => {
				Log.Error("Connecting failed: {Error}.", err);
				done.Set();
			},
			verbose: true);

		Assert.IsTrue(done.Wait(20000), "Took too long to receive completion.");

		Log.Information("Closing client TLS connection...");
		clientSsl.Close("Normal close.");
		Log.Information("Stopping listener...");
		listener.Stop();
		Log.Information("Checking received data...");

		Assert.AreEqual(shouldConnectSuccessfully ? sent : LogRecord.NoData.ToArray(), received.ToArray());
	}
}
