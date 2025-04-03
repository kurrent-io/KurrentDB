// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Infrastructure;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.TransactionLog.Checkpoint;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class RandomizedElectionsTestCase {
	protected readonly int RndSeed;
	protected readonly Random Rnd;

	protected readonly double HttpLossProbability;
	protected readonly double HttpDupProbability;
	protected readonly int HttpMaxDelay;

	protected readonly int InstancesCnt;

	private readonly int _maxIterCnt;

	private readonly int _timerMinDelay;
	private readonly int _timerMaxDelay;

	private static readonly IPEndPoint BaseEndPoint = new IPEndPoint(IPAddress.Loopback, 1000);

	public readonly RandomTestRunner Runner;
	public readonly IRandTestItemProcessor Logger;
	public IRandTestFinishCondition FinishCondition;

	private readonly List<ElectionsInstance> _instances = new List<ElectionsInstance>();

	private readonly bool _isReadOnlyReplica;

	public RandomizedElectionsTestCase(int maxIterCnt,
		int instancesCnt,
		double httpLossProbability,
		double httpDupProbability,
		int httpMaxDelay,
		int timerMinDelay,
		int timerMaxDelay,
		int? rndSeed = null,
		bool isReadOnlyReplica = false) {
		RndSeed = rndSeed ?? Math.Abs(Environment.TickCount);
		Rnd = new Random(RndSeed);

		_maxIterCnt = maxIterCnt;
		InstancesCnt = instancesCnt;
		HttpLossProbability = httpLossProbability;
		HttpDupProbability = httpDupProbability;
		HttpMaxDelay = httpMaxDelay;
		_timerMinDelay = timerMinDelay;
		_timerMaxDelay = timerMaxDelay;
		_isReadOnlyReplica = isReadOnlyReplica;

		Runner = new RandomTestRunner(_maxIterCnt);
		Logger = new ElectionsLogger();
	}

	public virtual void Init() {
		var sendOverHttpHandler = GetSendOverHttpProcessor();

		for (int i = 0; i < InstancesCnt; ++i) {
			var inputBus = new SynchronousScheduler($"ELECTIONS-INPUT-BUS-{i}");
			var outputBus = new SynchronousScheduler($"ELECTIONS-OUTPUT-BUS-{i}");
			var endPoint = new IPEndPoint(BaseEndPoint.Address, BaseEndPoint.Port + i);
			var memberInfo = MemberInfo.Initial(Guid.NewGuid(), DateTime.UtcNow, VNodeState.Unknown, true,
				endPoint, endPoint, endPoint, endPoint, endPoint, null, 0, 0, 0, false);
			_instances.Add(new ElectionsInstance(memberInfo.InstanceId, endPoint, inputBus, outputBus));

			sendOverHttpHandler.RegisterEndPoint(endPoint, inputBus);

			var electionsService = new Core.Services.ElectionsService(outputBus,
				memberInfo,
				InstancesCnt,
				new InMemoryCheckpoint(),
				new InMemoryCheckpoint(),
				new InMemoryCheckpoint(-1),
				new FakeEpochManager(),
				() => -1, 0, new FakeTimeProvider(),
				TimeSpan.FromMilliseconds(1_000));
			electionsService.SubscribeMessages(inputBus);

			outputBus.Subscribe(sendOverHttpHandler);
			outputBus.Subscribe(new TimerMessageProcessor(Rnd,
				Runner,
				endPoint,
				inputBus,
				_timerMinDelay,
				_timerMaxDelay));
			outputBus.Subscribe(new InnerBusMessagesProcessor(Runner, endPoint, inputBus));
		}
	}

	protected virtual SendOverGrpcProcessor GetSendOverHttpProcessor() {
		return new SendOverGrpcProcessor(Rnd,
			Runner,
			HttpLossProbability,
			HttpDupProbability,
			HttpMaxDelay);
	}

	protected virtual IRandTestFinishCondition GetFinishCondition() {
		return new ElectionsProgressCondition(InstancesCnt);
	}

	protected virtual IRandTestItemProcessor[] GetAdditionalProcessors() {
		return new IRandTestItemProcessor[] { };
	}

	protected virtual GossipMessage.GossipUpdated GetInitialGossipFor(ElectionsInstance instance,
		List<ElectionsInstance> allInstances) {
		var members = allInstances.Select(
			x => MemberInfo.ForVNode(x.InstanceId, DateTime.UtcNow, VNodeState.Unknown, true,
				x.EndPoint, null, x.EndPoint, null,
				x.EndPoint, null, 0, 0, -1, 0, 0, -1, -1, Guid.Empty, 0, false));
		var gossip = new GossipMessage.GossipUpdated(new ClusterInfo(members.ToArray()));
		return gossip;
	}

	public bool Run() {
		FinishCondition = GetFinishCondition();

		foreach (var instance in _instances) {
			Runner.Enqueue(instance.EndPoint, new SystemMessage.SystemInit(), instance.InputBus);
			Runner.Enqueue(instance.EndPoint, new ElectionMessage.StartElections(), instance.InputBus);

			var gossip = GetInitialGossipFor(instance, _instances);
			Runner.Enqueue(instance.EndPoint, gossip, instance.InputBus);
		}

		var additionalProcessors = GetAdditionalProcessors();
		var processors = new[] {Logger}.Union(additionalProcessors);

		var isGood = Runner.Run(FinishCondition, processors.ToArray());

		if (!isGood) {
			Console.WriteLine("Unsuccessful run. Parameters:\n"
			                  + "rndSeed: {0}\n"
			                  + "maxIterCnt = {1}\n"
			                  + "instancesCnt = {2}\n"
			                  + "httpLossProbability = {3}\n"
			                  + "httpMaxDelay = {4}\n"
			                  + "timerMinDelay = {5}\n"
			                  + "timerMaxDelay = {6}\n",
				RndSeed,
				_maxIterCnt,
				InstancesCnt,
				HttpLossProbability,
				HttpMaxDelay,
				_timerMinDelay,
				_timerMaxDelay);
		}

		return isGood;
	}
}
