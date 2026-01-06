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
using KurrentDB.Transport.Tcp;
using NUnit.Framework;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class ssl_connection {
	private static readonly ILogger Log = Serilog.Log.ForContext<ssl_connection>();
	private IPAddress _ip;
	private int _port;

	[SetUp]
	public void SetUp() {
		_ip = IPAddress.Loopback;
		_port = PortsHelper.GetAvailablePort(_ip);
	}

	[Test]
	public void should_connect_to_each_other_and_send_data() {
		var serverEndPoint = new IPEndPoint(_ip, _port);
		X509Certificate2 cert = ssl_connections.GetServerCertificate();

		var sent = new byte[1000];
		new Random().NextBytes(sent);

		using var received = new MemoryStream();

		var done = new ManualResetEventSlim();

		var listener = new TcpServerListener(serverEndPoint);
		listener.StartListening((endPoint, socket) => {
			var ssl = TcpConnectionSsl.CreateServerFromSocket(Guid.NewGuid(), endPoint, socket,
				() => cert, null, delegate { return (true, null); },
				verbose: true);
			ssl.ConnectionClosed += (x, y) => done.Set();
			if (ssl.IsClosed)
				done.Set();

			Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback = null;
			callback = (x, y) => {
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
			delegate { return (true, null); },
			null,
			new TcpClientConnector(),
			TcpConnectionManager.ConnectionTimeout,
			conn => {
				Log.Information("Sending bytes...");
				conn.EnqueueSend(new[] { new ArraySegment<byte>(sent) });
			},
			(conn, err) => {
				Log.Error("Connecting failed: {0}.", err);
				done.Set();
			},
			verbose: true);

		Assert.IsTrue(done.Wait(20000), "Took too long to receive completion.");

		Log.Information("Stopping listener...");
		listener.Stop();
		Log.Information("Closing client TLS connection...");
		clientSsl.Close("Normal close.");
		Log.Information("Checking received data...");
		Assert.AreEqual(sent, received.ToArray());
	}
}
