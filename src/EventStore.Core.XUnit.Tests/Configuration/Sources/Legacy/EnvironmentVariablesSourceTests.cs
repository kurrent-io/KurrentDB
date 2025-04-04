// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration.Sources.Legacy;

public class EnvironmentVariablesSourceTests {
	[Theory]
	[InlineData("EVENTSTORE_STREAM_INFO_CACHE_CAPACITY", "KurrentDB:StreamInfoCacheCapacity")]
	public void TransformsEventStoreEnvVars(string key, string normalizedKey) {
		// Arrange
		var environment = new Dictionary<string, string> { { key, key } };

		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyEventStoreEnvironmentVariables(environment)
			.Build();

		// Assert
		configuration
			.AsEnumerable()
			.Any(x => x.Key == normalizedKey)
			.Should().BeTrue();
	}

	[Theory]
	[InlineData("StreamInfoCacheCapacity")]
	[InlineData("stream-info-cache-capacity")]
	[InlineData("EventStore:Cluster:StreamInfoCacheCapacity")]
	[InlineData("UNSUPPORTED_EVENTSTORE_TCP_API_ENABLED")]
	public void IgnoresOtherEnvVars(string key) {
		// Arrange
		var environment = new Dictionary<string, string> { { key, key } };

		// Act
		var configuration = new ConfigurationBuilder()
			.AddLegacyEventStoreEnvironmentVariables(environment)
			.Build();

		// Assert
		configuration.AsEnumerable()
			.Any().Should().BeFalse();
	}
}
