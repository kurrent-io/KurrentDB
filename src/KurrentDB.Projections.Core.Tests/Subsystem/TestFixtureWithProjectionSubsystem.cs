// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Options;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Core.Tests.TransactionLog;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Subsystem;

public class TestFixtureWithProjectionSubsystem {
	private StandardComponents _standardComponents;

	protected ProjectionsSubsystem Subsystem;
	protected const int WaitTimeoutMs = 3000;

	private readonly ManualResetEvent _stopReceived = new ManualResetEvent(false);
	private ProjectionSubsystemMessage.StopComponents _lastStopMessage;

	private readonly ManualResetEvent _startReceived = new ManualResetEvent(false);
	private ProjectionSubsystemMessage.StartComponents _lastStartMessage;

	protected Task Started { get; private set; }

	private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();

	private static StandardComponents CreateStandardComponents() {
		var dbConfig = TFChunkHelper.CreateDbConfig(Path.Combine(Path.GetTempPath(), "ES-Projections"), 0);
		var mainQueue = new QueuedHandlerThreadPool(
			new AdHocHandler<Message>(_ => {
				/* Ignore messages */
			}), "MainQueue", new(), new());
		var mainBus = new InMemoryBus("mainBus");
		var statsManager = new QueueStatsManager();
		var threadBasedScheduler = new ThreadBasedScheduler(statsManager, new());
		var timerService = new TimerService(threadBasedScheduler);

		return new StandardComponents(dbConfig, mainQueue, mainBus,
			timerService, timeProvider: null, httpForwarder: null, httpServices: [],
			networkSendService: null, queueStatsManager: statsManager,
			trackers: new(), new());
	}

	[OneTimeSetUp]
	public void SetUp() {
		_standardComponents = CreateStandardComponents();

		var builder = WebApplication.CreateBuilder();
		builder.Services.AddGrpc();
		builder.Services.AddSingleton(_standardComponents);

		Subsystem = new(new(1, ProjectionType.All, true, TimeSpan.FromSeconds(3), true, 500, 250, Opts.MaxProjectionStateSizeDefault));

		Subsystem.ConfigureServices(builder.Services, new ConfigurationBuilder().Build());
		Subsystem.ConfigureApplication(builder.Build().UseRouting(), EmptyConfiguration);

		// Unsubscribe from the actual components so we can test in isolation
		Subsystem.LeaderInputBus.Unsubscribe<ProjectionSubsystemMessage.ComponentStarted>(Subsystem);
		Subsystem.LeaderInputBus.Unsubscribe<ProjectionSubsystemMessage.ComponentStopped>(Subsystem);

		Subsystem.LeaderInputBus.Subscribe(new AdHocHandler<Message>(msg => {
			switch (msg) {
				case ProjectionSubsystemMessage.StartComponents start: {
					_lastStartMessage = start;
					_startReceived.Set();
					break;
				}
				case ProjectionSubsystemMessage.StopComponents stop: {
					_lastStopMessage = stop;
					_stopReceived.Set();
					break;
				}
			}
		}));

		Started = Subsystem.Start();

		Given();
	}

	[OneTimeTearDown]
	public void TearDown() {
		_standardComponents.TimerService.Dispose();
	}

	protected virtual void Given() {
	}

	protected ProjectionSubsystemMessage.StartComponents WaitForStartMessage(string timeoutMsg = null, bool failOnTimeout = true) {
		timeoutMsg ??= "Timed out waiting for Start Components";
		if (_startReceived.WaitOne(WaitTimeoutMs))
			return _lastStartMessage;
		if (failOnTimeout)
			Assert.Fail(timeoutMsg);
		return null;
	}

	protected ProjectionSubsystemMessage.StopComponents WaitForStopMessage(string timeoutMsg = null) {
		timeoutMsg ??= "Timed out waiting for Stop Components";
		if (_stopReceived.WaitOne(WaitTimeoutMs)) {
			return _lastStopMessage;
		}

		Assert.Fail(timeoutMsg);
		return null;
	}

	protected void ResetMessageEvents() {
		_stopReceived.Reset();
		_startReceived.Reset();
		_lastStopMessage = null;
		_lastStartMessage = null;
	}
}
