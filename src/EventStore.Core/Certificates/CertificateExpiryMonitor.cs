// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using Serilog;

namespace EventStore.Core.Certificates;

public class CertificateExpiryMonitor :
	IHandle<SystemMessage.SystemStart>,
	IHandle<MonitoringMessage.CheckCertificateExpiry> {

	private static readonly TimeSpan _warningThreshold = TimeSpan.FromDays(30);
	private static readonly TimeSpan _interval = TimeSpan.FromDays(1);

	private readonly Func<X509Certificate2> _getCertificate;
	private readonly IPublisher _publisher;
	private readonly TimerMessage.Schedule _nodeCertificateExpirySchedule;
	private readonly ILogger _logger;

	public CertificateExpiryMonitor(
		IPublisher publisher,
		Func<X509Certificate2> getCertificate,
		ILogger logger) {

		Ensure.NotNull(publisher, nameof(publisher));
		Ensure.NotNull(getCertificate, nameof(getCertificate));
		Ensure.NotNull(logger, nameof(logger));

		_publisher = publisher;
		_getCertificate = getCertificate;
		_logger = logger;
		_nodeCertificateExpirySchedule = TimerMessage.Schedule.Create(
			_interval,
			publisher,
			new MonitoringMessage.CheckCertificateExpiry());
	}

	public void Handle(SystemMessage.SystemStart message) {
		_publisher.Publish(new MonitoringMessage.CheckCertificateExpiry());
	}

	public void Handle(MonitoringMessage.CheckCertificateExpiry message) {
		var certificate = _getCertificate();

		if (certificate != null) {
			var certExpiryDate = certificate.NotAfter;
			var timeUntilExpiry = certExpiryDate - DateTime.Now;

			if (timeUntilExpiry <= _warningThreshold) {
				_logger.Warning(
					"Certificates are going to expire in {daysUntilExpiry:N1} days",
					timeUntilExpiry.TotalDays);
			}
		}

		_publisher.Publish(_nodeCertificateExpirySchedule);
	}
}
