// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.V2;
using KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

namespace KurrentDB.Projections.V2.Tests.Unit;

public class ReadStrategyFactoryTests {
	sealed class NoOpPublisher : IPublisher {
		public void Publish(Message message) { }
	}

	static readonly IPublisher Bus = new NoOpPublisher();
	static readonly ClaimsPrincipal User = new(new ClaimsIdentity());

	[Test]
	public async Task all_streams_all_events_creates_filtered_all_strategy() {
		var sources = new QuerySourcesDefinition {
			AllStreams = true,
			AllEvents = true
		};

		var strategy = ReadStrategyFactory.Create(sources, Bus, User);

		await Assert.That(strategy).IsTypeOf<FilteredAllReadStrategy>();
	}

	[Test]
	public async Task all_streams_with_event_filter_creates_filtered_all_strategy() {
		var sources = new QuerySourcesDefinition {
			AllStreams = true,
			Events = ["OrderPlaced", "OrderShipped"]
		};

		var strategy = ReadStrategyFactory.Create(sources, Bus, User);

		await Assert.That(strategy).IsTypeOf<FilteredAllReadStrategy>();
	}

	[Test]
	public async Task specific_streams_creates_filtered_all_strategy() {
		var sources = new QuerySourcesDefinition {
			Streams = ["stream-a", "stream-b"]
		};

		var strategy = ReadStrategyFactory.Create(sources, Bus, User);

		await Assert.That(strategy).IsTypeOf<FilteredAllReadStrategy>();
	}

	[Test]
	public async Task categories_creates_filtered_all_strategy() {
		var sources = new QuerySourcesDefinition {
			Categories = ["order", "invoice"]
		};

		var strategy = ReadStrategyFactory.Create(sources, Bus, User);

		await Assert.That(strategy).IsTypeOf<FilteredAllReadStrategy>();
	}

	[Test]
	public Task unsupported_source_throws() {
		var sources = new QuerySourcesDefinition();

		Assert.Throws<ArgumentException>(() => ReadStrategyFactory.Create(sources, Bus, User));
		return Task.CompletedTask;
	}
}
