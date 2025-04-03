// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Options;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Telemetry;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messaging;
using EventStore.Projections.Core.Metrics;
using EventStore.Projections.Core.Services.Http;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core;

public class ProjectionManagerNode {
	public static void CreateManagerService(
		StandardComponents standardComponents,
		ProjectionsStandardComponents projectionsStandardComponents,
		IDictionary<Guid, IPublisher> queues,
		TimeSpan projectionQueryExpiry,
		IProjectionTracker projectionTracker) {
		IPublisher inputQueue = projectionsStandardComponents.LeaderInputQueue;
		IPublisher outputQueue = projectionsStandardComponents.LeaderOutputQueue;
		var ioDispatcher = new IODispatcher(outputQueue, inputQueue, true);

		var projectionsController = new ProjectionsController(
			standardComponents.HttpForwarder,
			inputQueue,
			standardComponents.NetworkSendService);

		var forwarder = new RequestResponseQueueForwarder(
			inputQueue: projectionsStandardComponents.LeaderInputQueue,
			externalRequestQueue: standardComponents.MainQueue);

		if (projectionsStandardComponents.RunProjections != ProjectionType.None) {
			foreach (var httpService in standardComponents.HttpServices) {
				httpService.SetupController(projectionsController);
			}
		}

		var projectionManagerMessageDispatcher = new ProjectionManagerMessageDispatcher(queues);

		var projectionManager = new ProjectionManager(
			inputQueue,
			outputQueue,
			queues,
			new RealTimeProvider(),
			projectionsStandardComponents.RunProjections,
			ioDispatcher,
			projectionQueryExpiry,
			projectionTracker);

		SubscribeMainBus(
			projectionsStandardComponents.LeaderInputBus,
			projectionManager,
			projectionsStandardComponents.RunProjections,
			ioDispatcher,
			projectionManagerMessageDispatcher);


		SubscribeOutputBus(standardComponents, projectionsStandardComponents, forwarder, ioDispatcher);
	}

	private static void SubscribeMainBus(
		ISubscriber mainBus,
		ProjectionManager projectionManager,
		ProjectionType runProjections,
		IODispatcher ioDispatcher,
		ProjectionManagerMessageDispatcher projectionManagerMessageDispatcher) {
		if (runProjections >= ProjectionType.System) {
			mainBus.Subscribe<ProjectionManagementMessage.Command.Post>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.PostBatch>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.UpdateQuery>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.GetQuery>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.Delete>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.GetStatistics>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.GetState>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.GetResult>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.Disable>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.Enable>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.Abort>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.SetRunAs>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.Reset>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.GetConfig>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Command.UpdateConfig>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Internal.CleanupExpired>(projectionManager);
			mainBus.Subscribe<ProjectionManagementMessage.Internal.Deleted>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.Started>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.Stopped>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.Faulted>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.Prepared>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.StateReport>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.ResultReport>(projectionManager);
			mainBus.Subscribe<CoreProjectionStatusMessage.StatisticsReport>(projectionManager);
			mainBus.Subscribe<ProjectionSubsystemMessage.StartComponents>(projectionManager);
			mainBus.Subscribe<ProjectionSubsystemMessage.StopComponents>(projectionManager);
		}

		mainBus.Subscribe<ClientMessage.WriteEventsCompleted>(projectionManager);
		mainBus.Subscribe<ClientMessage.DeleteStreamCompleted>(projectionManager);
		mainBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(projectionManager);
		mainBus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(projectionManager);
		mainBus.Subscribe<TelemetryMessage.Request>(projectionManager);

		mainBus.Subscribe(ioDispatcher.Awaker);
		mainBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(ioDispatcher.BackwardReader);
		mainBus.Subscribe<ClientMessage.NotHandled>(ioDispatcher.BackwardReader);
		mainBus.Subscribe(ioDispatcher.ForwardReader);
		mainBus.Subscribe(ioDispatcher.StreamDeleter);
		mainBus.Subscribe(ioDispatcher.Writer);
		mainBus.Subscribe(ioDispatcher.EventReader);
		mainBus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher);
		mainBus.Subscribe<ClientMessage.NotHandled>(ioDispatcher);

		mainBus.Subscribe(projectionManagerMessageDispatcher);
	}

	private static void SubscribeOutputBus(
		StandardComponents standardComponents,
		ProjectionsStandardComponents projectionsStandardComponents,
		RequestResponseQueueForwarder forwarder,
		IODispatcher ioDispatcher) {
		var managerOutput = projectionsStandardComponents.LeaderOutputBus;
		managerOutput.Subscribe<ClientMessage.ReadEvent>(forwarder);
		managerOutput.Subscribe<ClientMessage.ReadStreamEventsBackward>(forwarder);
		managerOutput.Subscribe<ClientMessage.ReadStreamEventsForward>(forwarder);
		managerOutput.Subscribe<ClientMessage.WriteEvents>(forwarder);
		managerOutput.Subscribe<ClientMessage.DeleteStream>(forwarder);
		managerOutput.Subscribe(Forwarder.Create<Message>(projectionsStandardComponents.LeaderInputQueue));
		managerOutput.Subscribe<ClientMessage.NotHandled>(ioDispatcher);

		managerOutput.Subscribe<TimerMessage.Schedule>(standardComponents.TimerService);
		managerOutput.Subscribe(Forwarder.Create<AwakeServiceMessage.SubscribeAwake>(standardComponents.MainQueue));
		managerOutput.Subscribe(
			Forwarder.Create<AwakeServiceMessage.UnsubscribeAwake>(standardComponents.MainQueue));
		managerOutput.Subscribe<SystemMessage.SubSystemInitialized>(forwarder);

		// self forward all
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<SystemMessage.StateChangeMessage>(projectionsStandardComponents.LeaderInputQueue));
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<SystemMessage.SystemCoreReady>(projectionsStandardComponents.LeaderInputQueue));
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<SystemMessage.EpochWritten>(projectionsStandardComponents.LeaderInputQueue));
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<ProjectionCoreServiceMessage.SubComponentStarted>(projectionsStandardComponents
				.LeaderInputQueue));
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<ProjectionCoreServiceMessage.SubComponentStopped>(projectionsStandardComponents
				.LeaderInputQueue));
		standardComponents.MainBus.Subscribe(
			Forwarder.Create<TelemetryMessage.Request>(projectionsStandardComponents.LeaderInputQueue));
		projectionsStandardComponents.LeaderInputBus.Subscribe(new UnwrapEnvelopeHandler());
	}
}
