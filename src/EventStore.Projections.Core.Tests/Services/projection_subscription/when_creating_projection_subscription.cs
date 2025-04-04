// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_subscription;

[TestFixture]
public class when_creating_projection_subscription {
	[Test]
	public void it_can_be_created() {
		new ReaderSubscription(
			"Test Subscription",
			new FakePublisher(),
			Guid.NewGuid(),
			CheckpointTag.FromPosition(0, 0, -1),
			CreateReaderStrategy(),
			new FakeTimeProvider(),
			1000,
			2000,
			10000,
			false,
			null,
			false);
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ReaderSubscription(
				"Test Subscription",
				null,
				Guid.NewGuid(),
				CheckpointTag.FromPosition(0, 0, -1),
				CreateReaderStrategy(),
				new FakeTimeProvider(),
				1000,
				2000,
				10000,
				false,
				null,
				false);
		});
	}

	[Test]
	public void null_checkpoint_strategy_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ReaderSubscription(
				"Test Subscription",
				new FakePublisher(),
				Guid.NewGuid(),
				CheckpointTag.FromPosition(0, 0, -1),
				null,
				new FakeTimeProvider(),
				1000,
				2000,
				10000,
				false,
				null,
				false);
		});
	}

	[Test]
	public void null_time_provider_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ReaderSubscription(
				"Test Subscription",
				new FakePublisher(),
				Guid.NewGuid(),
				CheckpointTag.FromPosition(0, 0, -1),
				CreateReaderStrategy(),
				null,
				1000,
				2000,
				10000,
				false,
				null,
				false);
		});
	}

	private IReaderStrategy CreateReaderStrategy() {
		var result = new SourceDefinitionBuilder();
		result.FromAll();
		result.AllEvents();
		return ReaderStrategy.Create(
			"test",
			0,
			result.Build(),
			new RealTimeProvider(),
			stopOnEof: false,
			runAs: null);
	}
}
