// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.PeriodicLogs;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests.Fakes;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.PeriodicLogs;


public class PeriodicallyLoggingServiceTests {

	private FakePublisher _publisher;
	private FakeLogger _logger;
	private string _esVersion;

	[SetUp]
	public void SetUp() {
		_publisher = new();
		_logger = new FakeLogger();
		_esVersion = "0.0.0.0";
	}

	[Test]
	public void on_start() {
		// given
		var sut = new PeriodicallyLoggingService(_publisher, _esVersion, _logger);

		// when
		sut.Handle(new SystemMessage.SystemStart());

		// then
		Assert.IsInstanceOf<MonitoringMessage.CheckEsVersion>(_publisher.Messages.Single());
		Assert.IsEmpty(_logger.LogMessages);
	}

	[Test]
	public void log_es_version_periodically() {
		// given
		var sut = new PeriodicallyLoggingService(_publisher, _esVersion, _logger);

		// when
		sut.Handle(new MonitoringMessage.CheckEsVersion());

		// then
		var schedule = (TimerMessage.Schedule)_publisher.Messages.Single();
		Assert.AreEqual(TimeSpan.FromHours(12), schedule.TriggerAfter);
		Assert.IsInstanceOf<MonitoringMessage.CheckEsVersion>(schedule.ReplyMessage);

		var logMessage = _logger.LogMessages.Single();
		Assert.AreEqual($"Current version of KurrentDB is : \"0.0.0.0\" ", logMessage.RenderMessage());
	}

}
