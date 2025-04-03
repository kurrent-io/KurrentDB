// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Certificates;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.Transport.Tcp;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests.when_building;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_ssl_enabled_and_using_a_security_certificate_from_file<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly IPEndPoint _internalSecTcp = new(IPAddress.Parse("127.0.1.15"), 1114);
	private readonly IPEndPoint _externalSecTcp = new(IPAddress.Parse("127.0.1.15"), 1115);

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {

		return options.WithReplicationEndpointOn(_internalSecTcp).WithExternalTcpOn(_externalSecTcp) with {
			CertificateFile = new() {
				CertificateFile = GetCertificatePath(),
				CertificatePrivateKeyFile = string.Empty,
				CertificatePassword = "password"
			}
		};
	}

	[Test]
	public void should_set_certificate() {
		Assert.AreNotEqual("n/a", _options.Certificate == null ? "n/a" : _options.Certificate.ToString());
	}

	[Test]
	public void should_set_internal_secure_tcp_endpoint() {
		Assert.AreEqual(_internalSecTcp, _node.NodeInfo.InternalSecureTcp);
	}

	private string GetCertificatePath() {
		var filePath = Path.Combine(PathName, $"ES-cert-{Guid.NewGuid()}.p12");
		var cert = ssl_connections.GetUntrustedCertificate();

		using var fileStream = File.Create(filePath);
		fileStream.Write(cert.ExportToPkcs12());

		return filePath;
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_ssl_enabled_and_using_a_security_certificate<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly IPEndPoint _internalSecTcp = new(IPAddress.Parse("127.0.1.15"), 1114);
	private readonly IPEndPoint _externalSecTcp = new(IPAddress.Parse("127.0.1.15"), 1115);
	private readonly X509Certificate2 _certificate = ssl_connections.GetServerCertificate();

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.WithReplicationEndpointOn(_internalSecTcp)
			.WithExternalTcpOn(_externalSecTcp)
			.Secure(new X509Certificate2Collection(ssl_connections.GetRootCertificate()), _certificate);
	}

	[Test]
	public void should_set_certificate() {
		Assert.AreNotEqual("n/a", _options.Certificate == null ? "n/a" : _options.Certificate.ToString());
	}

	[Test]
	public void should_set_internal_secure_tcp_endpoint() {
		Assert.AreEqual(_internalSecTcp, _node.NodeInfo.InternalSecureTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_secure_tcp_endpoints_and_no_certificates<TLogFormat, TStreamId> {
	private ClusterVNodeOptions _options;
	private Exception _caughtException;

	[OneTimeSetUp]
	public void SetUp() {
		var baseIpAddress = IPAddress.Parse("127.0.1.15");
		var internalSecTcp = new IPEndPoint(baseIpAddress, 1114);
		var externalSecTcp = new IPEndPoint(baseIpAddress, 1115);
		_options = new ClusterVNodeOptions()
			.ReduceMemoryUsageForTests()
			.RunInMemory()
			.WithReplicationEndpointOn(internalSecTcp)
			.WithExternalTcpOn(externalSecTcp);
		try {
			_ = new ClusterVNode<TStreamId>(_options, LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory,
				certificateProvider: new OptionsCertificateProvider());
		} catch (Exception ex) {
			_caughtException = ex;
		}
	}

	[Test]
	public void should_throw_an_exception() {
		Assert.IsNotNull(_caughtException);
	}
}
