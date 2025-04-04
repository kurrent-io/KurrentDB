// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class DefaultValuesConfigurationSourceTests {
	[Fact]
	public void Adds() {
		// Arrange
		var defaults = ClusterVNodeOptions.DefaultValues.OrderBy(x => x.Key).ToList();

		// Act
		var configuration = new ConfigurationBuilder()
			.AddEventStoreDefaultValues()
			.Build()
			.GetSection(EventStoreConfigurationKeys.Prefix);

		// Assert
		foreach (var (key, expectedValue) in defaults) {
			configuration.GetValue<object>(key)
				.Should()
				.BeEquivalentTo(expectedValue?.ToString(), $"because {key} should be {expectedValue}");
		}
	}
}
