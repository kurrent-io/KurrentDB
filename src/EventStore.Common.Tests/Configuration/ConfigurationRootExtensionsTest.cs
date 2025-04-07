// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Tests.Configuration;

public class ConfigurationRootExtensionsTest {
	const string GOSSIP_SEED = "GossipSeed";

	[Fact]
	public void successful_comma_separated_value() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodeb.eventstore.test:2113,nodec.eventstore.test:3113" }
		};

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(config)
			.Build();

		var values = configuration.GetCommaSeparatedValueAsArray("GossipSeed");

		Assert.Equal(2, values.Length);
	}

	[Fact]
	public void invalid_delimiter() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodeb.eventstore.test:2113;nodec.eventstore.test:3113" }
		};

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(config)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			configuration.GetCommaSeparatedValueAsArray("GossipSeed"));
	}

	[Fact]
	public void mixed_invalid_delimiter() {
		var config = new Dictionary<string, string?> {
			{ GOSSIP_SEED, "nodea.eventstore.test:2113,nodeb.eventstore.test:2113;nodec.eventstore.test:3113" }
		};

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(config)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			configuration.GetCommaSeparatedValueAsArray("GossipSeed"));
	}
}
