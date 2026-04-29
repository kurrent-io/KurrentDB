// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using Serilog;

namespace KurrentDB.Core.Certificates;

public class CertificateExpiryMonitor :
	IHandle<SystemMessage.SystemStart>,
	IHandle<MonitoringMessage.CheckCertificateExpiry> {

	private static readonly TimeSpan _warningThreshold = TimeSpan.FromDays(30);
	private static readonly TimeSpan _interval = TimeSpan.FromDays(1);

	private readonly IReadOnlyList<Func<X509Certificate2>> _getCertificates;
	private readonly IPublisher _publisher;
	private readonly TimerMessage.Schedule _nodeCertificateExpirySchedule;
	private readonly ILogger _logger;

	public CertificateExpiryMonitor(
		IPublisher publisher,
		ILogger logger,
		params Func<X509Certificate2>[] getCertificates) {

		Ensure.NotNull(publisher, nameof(publisher));
		Ensure.NotNull(logger, nameof(logger));
		Ensure.NotNull(getCertificates, nameof(getCertificates));

		_publisher = publisher;
		_getCertificates = getCertificates;
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
		// dedup so single cert mode (where multiple selectors resolve to the same cert) isn't double-logged
		HashSet<string> checkedThumbprints = null;

		foreach (var getCertificate in _getCertificates) {
			var certificate = getCertificate();
			if (certificate is null)
				continue;

			checkedThumbprints ??= [];
			if (!checkedThumbprints.Add(certificate.Thumbprint))
				continue;

			var timeUntilExpiry = certificate.NotAfter - DateTime.Now;
			if (timeUntilExpiry <= _warningThreshold) {
				_logger.Warning(
					"Certificate ({subject}, thumbprint {thumbprint}) is going to expire in {daysUntilExpiry:N1} days",
					certificate.SubjectName.Name, certificate.Thumbprint, timeUntilExpiry.TotalDays);
			}
		}

		_publisher.Publish(_nodeCertificateExpirySchedule);
	}
}
