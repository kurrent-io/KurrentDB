// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using EventStore.Core.Certificates;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;

namespace EventStore.Core.Tests.Certificates;


public class CertificateExpiryMonitorTests : with_certificates {
	private FakePublisher _publisher;
	private FakeLogger _logger;

	private static X509Certificate2 GenCertificate(TimeSpan timeUntilExpiry) {
		using var rsa = RSA.Create();
		var certificate = new CertificateRequest(GenerateSubject(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
			.CreateSelfSigned(
				notBefore: DateTime.Now,
				notAfter: DateTime.Now + timeUntilExpiry);
		return certificate;
	}

	[SetUp]
	public void SetUp() {
		_publisher = new();
		_logger = new();
	}

	[Test]
	public void on_start() {
		// given
		var certificate = GenCertificate(TimeSpan.FromDays(60));
		var sut = new CertificateExpiryMonitor(_publisher, () => certificate, _logger);

		// when
		sut.Handle(new SystemMessage.SystemStart());

		// then
		Assert.IsInstanceOf<MonitoringMessage.CheckCertificateExpiry>(_publisher.Messages.Single());
		Assert.IsEmpty(_logger.LogMessages);
	}

	[Test]
	public void certificate_is_going_to_expire_within_30_days() {
		// given
		var certificate = GenCertificate(TimeSpan.FromDays(29));
		var sut = new CertificateExpiryMonitor(_publisher, () => certificate, _logger);

		// when
		sut.Handle(new MonitoringMessage.CheckCertificateExpiry());

		// then
		var schedule = (TimerMessage.Schedule)_publisher.Messages.Single();
		Assert.AreEqual(TimeSpan.FromHours(24), schedule.TriggerAfter);
		Assert.IsInstanceOf<MonitoringMessage.CheckCertificateExpiry>(schedule.ReplyMessage);

		var logMessage = _logger.LogMessages.Single();
		Assert.AreEqual(
			"Certificates are going to expire in 29.0 days",
			logMessage.RenderMessage());
	}

	[Test]
	public void certificate_is_not_going_to_expire_within_30_days() {
		// given
		var certificate = GenCertificate(TimeSpan.FromDays(31));
		var sut = new CertificateExpiryMonitor(_publisher, () => certificate, _logger);

		// when
		sut.Handle(new MonitoringMessage.CheckCertificateExpiry());

		// then
		var schedule = (TimerMessage.Schedule)_publisher.Messages.Single();
		Assert.AreEqual(TimeSpan.FromHours(24), schedule.TriggerAfter);
		Assert.IsInstanceOf<MonitoringMessage.CheckCertificateExpiry>(schedule.ReplyMessage);

		Assert.IsEmpty(_logger.LogMessages);
	}
}
