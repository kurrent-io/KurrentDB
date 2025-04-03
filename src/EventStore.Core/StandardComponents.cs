// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Configuration;
using EventStore.Core.Bus;
using EventStore.Core.Metrics;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core;

public class StandardComponents {
	private readonly TFChunkDbConfig _dbConfig;
	private readonly IPublisher _mainQueue;
	private readonly ISubscriber _mainBus;
	private readonly TimerService _timerService;
	private readonly ITimeProvider _timeProvider;
	private readonly IHttpForwarder _httpForwarder;
	private readonly IHttpService[] _httpServices;
	private readonly IPublisher _networkSendService;
	private readonly QueueStatsManager _queueStatsManager;

	public StandardComponents(
		TFChunkDbConfig dbConfig,
		IPublisher mainQueue,
		ISubscriber mainBus,
		TimerService timerService,
		ITimeProvider timeProvider,
		IHttpForwarder httpForwarder,
		IHttpService[] httpServices,
		IPublisher networkSendService,
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers,
		MetricsConfiguration metricsConfiguration) {
		_dbConfig = dbConfig;
		_mainQueue = mainQueue;
		_mainBus = mainBus;
		_timerService = timerService;
		_timeProvider = timeProvider;
		_httpForwarder = httpForwarder;
		_httpServices = httpServices;
		_networkSendService = networkSendService;
		_queueStatsManager = queueStatsManager;
		QueueTrackers = trackers;
		MetricsConfiguration = metricsConfiguration;
	}

	public TFChunkDbConfig DbConfig {
		get { return _dbConfig; }
	}

	public IPublisher MainQueue {
		get { return _mainQueue; }
	}

	public ISubscriber MainBus {
		get { return _mainBus; }
	}

	public TimerService TimerService {
		get { return _timerService; }
	}

	public ITimeProvider TimeProvider {
		get { return _timeProvider; }
	}

	public IHttpForwarder HttpForwarder {
		get { return _httpForwarder; }
	}

	public IHttpService[] HttpServices {
		get { return _httpServices; }
	}

	public IPublisher NetworkSendService {
		get { return _networkSendService; }
	}

	public QueueStatsManager QueueStatsManager {
		get { return _queueStatsManager; }
	}

	public MetricsConfiguration MetricsConfiguration { get; }

	public QueueTrackers QueueTrackers { get; private set; }
}
