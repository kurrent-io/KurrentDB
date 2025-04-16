// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messages;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using Serilog;

namespace KurrentDB.Core.Services.PeriodicLogs;

public class PeriodicallyLoggingService :
	IHandle<SystemMessage.SystemStart>,
	IHandle<MonitoringMessage.CheckEsVersion> {

	private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

	private readonly IPublisher _publisher;
	private readonly string _esVersion;
	private readonly ILogger _logger;
	private readonly TimerMessage.Schedule _esVersionScheduleLog;

	public PeriodicallyLoggingService(IPublisher publisher, string esVersion, ILogger logger) {
		_publisher = Ensure.NotNull(publisher);
		_esVersion = esVersion;
		_logger = Ensure.NotNull(logger);
		_esVersionScheduleLog = TimerMessage.Schedule.Create(Interval, publisher, new MonitoringMessage.CheckEsVersion());
	}

	public void Handle(SystemMessage.SystemStart message) {
		_publisher.Publish(new MonitoringMessage.CheckEsVersion());
	}

	public void Handle(MonitoringMessage.CheckEsVersion message) {
		_logger.Information("Current version of KurrentDB is : {dbVersion} ", _esVersion);
		_publisher.Publish(_esVersionScheduleLog);
	}
}
