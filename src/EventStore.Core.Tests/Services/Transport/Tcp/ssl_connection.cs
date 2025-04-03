// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.Tests.Helpers;
using EventStore.Transport.Tcp;
using NUnit.Framework;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class ssl_connections {
	private static readonly ILogger Log = Serilog.Log.ForContext<ssl_connections>();
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
		X509Certificate2 cert = GetServerCertificate();

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

	private static X509Certificate2 _root, _server, _otherServer, _untrusted;
	public static X509Certificate2 GetRootCertificate() {
		_root ??= GetCertificate("ca", loadKey: false);
		return new X509Certificate2(_root);
	}

	public static X509Certificate2 GetServerCertificate() {
		_server ??= GetCertificate("node1");
		return new X509Certificate2(_server);
	}

	public static X509Certificate2 GetOtherServerCertificate() {
		_otherServer ??= GetCertificate("node2");
		return new X509Certificate2(_otherServer);
	}

	public static X509Certificate2 GetUntrustedCertificate() {
		_untrusted ??= GetCertificate("untrusted");
		return new X509Certificate2(_untrusted);
	}

	private static X509Certificate2 GetCertificate(string name, bool loadKey = true) {
		const string resourcePath = "EventStore.Core.Tests.Services.Transport.Tcp.test_certificates";

		var certBytes = LoadResource($"{resourcePath}.{name}.{name}.crt");
		var certificate = X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(certBytes));

		if (!loadKey)
			return certificate;

		var keyBytes = LoadResource($"{resourcePath}.{name}.{name}.key");
		using var rsa = RSA.Create();
		rsa.ImportFromPem(Encoding.UTF8.GetString(keyBytes));

		using X509Certificate2 certWithKey = certificate.CopyWithPrivateKey(rsa);

		// recreate the certificate from a PKCS #12 bundle to work around: https://github.com/dotnet/runtime/issues/23749
		return new X509Certificate2(certWithKey.ExportToPkcs12(), string.Empty, X509KeyStorageFlags.Exportable);
	}

	private static byte[] LoadResource(string resource) {
		using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
		if (resourceStream == null)
			return null;

		using var memStream = new MemoryStream();
		resourceStream.CopyTo(memStream);
		return memStream.ToArray();
	}
}
