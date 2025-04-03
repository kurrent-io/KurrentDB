// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messages.EventReaders.Feeds;
using EventStore.Projections.Core.Messaging;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core;

public static class ProjectionCoreWorkersNode {
	public static Dictionary<Guid, CoreWorker> CreateCoreWorkers(
		StandardComponents standardComponents,
		ProjectionsStandardComponents projectionsStandardComponents) {
		var coreWorkers = new Dictionary<Guid, CoreWorker>();
		while (coreWorkers.Count < projectionsStandardComponents.ProjectionWorkerThreadCount) {
			var coreInputBus = new InMemoryBus("bus");
			var coreInputQueue = new QueuedHandlerThreadPool(coreInputBus,
				"Projection Core #" + coreWorkers.Count,
				standardComponents.QueueStatsManager,
				standardComponents.QueueTrackers,
				groupName: "Projection Core");
			var coreOutputBus = new InMemoryBus("output bus");
			var coreOutputQueue = new QueuedHandlerThreadPool(coreOutputBus,
				"Projection Core #" + coreWorkers.Count + " output",
				standardComponents.QueueStatsManager,
				standardComponents.QueueTrackers,
				groupName: "Projection Core");
			var workerId = Guid.NewGuid();
			var projectionNode = new ProjectionWorkerNode(
				workerId,
				standardComponents.DbConfig,
				inputQueue: coreInputQueue,
				outputQueue: coreOutputQueue,
				coreOutputBus,
				standardComponents.TimeProvider,
				projectionsStandardComponents.RunProjections,
				projectionsStandardComponents.FaultOutOfOrderProjections,
				projectionsStandardComponents.LeaderOutputQueue,
				projectionsStandardComponents);
			projectionNode.SetupMessaging(coreInputBus);

			var forwarder = new RequestResponseQueueForwarder(
				inputQueue: coreInputQueue,
				externalRequestQueue: standardComponents.MainQueue);
			// forwarded messages
			var coreOutput = projectionNode.CoreOutputBus;
			coreOutput.Subscribe<ClientMessage.ReadEvent>(forwarder);
			coreOutput.Subscribe<ClientMessage.ReadStreamEventsBackward>(forwarder);
			coreOutput.Subscribe<ClientMessage.ReadStreamEventsForward>(forwarder);
			coreOutput.Subscribe<ClientMessage.ReadAllEventsForward>(forwarder);
			coreOutput.Subscribe<ClientMessage.WriteEvents>(forwarder);
			coreOutput.Subscribe<ClientMessage.DeleteStream>(forwarder);
			coreOutput.Subscribe<ProjectionCoreServiceMessage.SubComponentStarted>(forwarder);
			coreOutput.Subscribe<ProjectionCoreServiceMessage.SubComponentStopped>(forwarder);

			if (projectionsStandardComponents.RunProjections >= ProjectionType.System) {
				coreOutput.Subscribe(
					Forwarder.Create<AwakeServiceMessage.SubscribeAwake>(standardComponents.MainQueue));
				coreOutput.Subscribe(
					Forwarder.Create<AwakeServiceMessage.UnsubscribeAwake>(standardComponents.MainQueue));
				coreOutput.Subscribe(
					Forwarder.Create<ProjectionSubsystemMessage.IODispatcherDrained>(projectionsStandardComponents
						.LeaderOutputQueue));
			}

			coreOutput.Subscribe<TimerMessage.Schedule>(standardComponents.TimerService);

			coreOutput.Subscribe(Forwarder.Create<Message>(coreInputQueue)); // forward all

			coreInputBus.Subscribe(new UnwrapEnvelopeHandler());

			coreWorkers.Add(workerId, new CoreWorker(workerId, coreInputQueue, coreOutputQueue));
		}

		var queues = coreWorkers.Select(v => v.Value.CoreInputQueue).ToArray();
		var coordinator = new ProjectionCoreCoordinator(
			projectionsStandardComponents.RunProjections,
			queues,
			projectionsStandardComponents.LeaderOutputQueue);

		coordinator.SetupMessaging(projectionsStandardComponents.LeaderInputBus);
		projectionsStandardComponents.LeaderInputBus.Subscribe(
			Forwarder.CreateBalancing<FeedReaderMessage.ReadPage>(coreWorkers
				.Select(x => x.Value.CoreInputQueue)
				.ToArray()));
		return coreWorkers;
	}
}

public class CoreWorker {
	public Guid WorkerId { get; }
	public IQueuedHandler CoreInputQueue { get; }
	public IQueuedHandler CoreOutputQueue { get; }

	public CoreWorker(Guid workerId, IQueuedHandler inputQueue, IQueuedHandler outputQueue) {
		WorkerId = workerId;
		CoreInputQueue = inputQueue;
		CoreOutputQueue = outputQueue;
	}

	public void Start() {
		CoreInputQueue.Start();
		CoreOutputQueue.Start();
	}

	public async Task Stop() {
		await CoreInputQueue.Stop();
		await CoreOutputQueue.Stop();
	}
}
