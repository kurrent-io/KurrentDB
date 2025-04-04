// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

[TestFixture]
public class elections_service_3_nodes_full_gossip_no_http_loss_no_dup {
	private RandomizedElectionsTestCase _randomCase;

	[SetUp]
	public void SetUp() {
		_randomCase = new RandomizedElectionsTestCase(ElectionParams.MaxIterationCount,
			instancesCnt: 3,
			httpLossProbability: 0.0,
			httpDupProbability: 0.0,
			httpMaxDelay: 20,
			timerMinDelay: 100,
			timerMaxDelay: 200);
		_randomCase.Init();
	}

	[Test, Category("LongRunning"), Category("Network")]
	public void should_always_arrive_at_coherent_results([Range(0, ElectionParams.TestRunCount - 1)]
		int run) {
		var success = _randomCase.Run();
		if (!success)
			_randomCase.Logger.LogMessages();
		Console.WriteLine("There were a total of {0} messages in this run.",
			_randomCase.Logger.ProcessedItems.Count());
		Assert.True(success);
	}
}
