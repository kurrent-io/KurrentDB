// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using FluentAssertions;
using KurrentDB.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Configuration.Sources;

public class DefaultValuesConfigurationSourceTests {
	[Fact]
	public void Adds() {
		// Arrange
		var defaults = ClusterVNodeOptions.DefaultValues.OrderBy(x => x.Key).ToList();

		// Act
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.Build()
			.GetSection(KurrentConfigurationKeys.Prefix);

		// Assert
		foreach (var (key, expectedValue) in defaults) {
			configuration.GetValue<object>(key)
				.Should()
				.BeEquivalentTo(expectedValue?.ToString(), $"because {key} should be {expectedValue}");
		}
	}
}
