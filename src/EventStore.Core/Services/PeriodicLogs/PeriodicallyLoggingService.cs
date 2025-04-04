// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using Serilog;

namespace EventStore.Core.Services.PeriodicLogs;

public class PeriodicallyLoggingService :
	IHandle<SystemMessage.SystemStart>,
	IHandle<MonitoringMessage.CheckEsVersion> {

	private static readonly TimeSpan _interval = TimeSpan.FromHours(12);

	private readonly IPublisher _publisher;
	private readonly string _esVersion;
	private readonly ILogger _logger;
	private readonly TimerMessage.Schedule _esVersionScheduleLog;

	public PeriodicallyLoggingService(IPublisher publisher, string esVersion, ILogger logger) {
		Ensure.NotNull(publisher, nameof(publisher));
		Ensure.NotNull(logger, nameof(logger));

		_publisher = publisher;
		_esVersion = esVersion;
		_logger = logger;
		_esVersionScheduleLog = TimerMessage.Schedule.Create(_interval, publisher,
			new MonitoringMessage.CheckEsVersion());
	}

	public void Handle(SystemMessage.SystemStart message) {
		_publisher.Publish(new MonitoringMessage.CheckEsVersion());
	}

	public void Handle(MonitoringMessage.CheckEsVersion message) {
		_logger.Information("Current version of Event Store is : {esVersion} ", _esVersion);
		_publisher.Publish(_esVersionScheduleLog);
	}

}
