// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Archive;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.TransactionLog.Scavenging;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ScavengeOptionsCalculatorTests {
	private const string SectionName = KurrentConfigurationKeys.Prefix;
	private static ScavengeOptionsCalculator GenSut(
		KeyValuePair<string, string?>[]? vNodeOptions = null,
		int? threshold = null) {

		vNodeOptions ??= [];
		var message = new ClientMessage.ScavengeDatabase(
			envelope: IEnvelope.NoOp,
			correlationId: Guid.NewGuid(),
			user: SystemAccounts.Anonymous,
			startFromChunk: 0,
			threads: 1,
			threshold: threshold,
			throttlePercent: 100,
			syncOnly: false);

		var config = new ConfigurationBuilder().AddInMemoryCollection(
				vNodeOptions.Append(new($"{SectionName}:ClusterSize", "1")))
			.Build();
		var options = ClusterVNodeOptions.FromConfiguration(config);
		var archiveOptions = config.GetSection($"{SectionName}:Archive").Get<ArchiveOptions>() ?? new();
		var sut = new ScavengeOptionsCalculator(options, archiveOptions, message);
		return sut;
	}

	[Fact]
	public void merging_enabled_by_default() {
		var sut = GenSut([
		]);

		Assert.True(sut.MergeChunks);
	}

	[Fact]
	public void merging_can_be_disabled() {
		var sut = GenSut([
			new($"{SectionName}:DisableScavengeMerging", "true"),
		]);

		Assert.False(sut.MergeChunks);
	}

	[Fact]
	public void merging_is_disabled_when_archiving_is_enabled() {
		var sut = GenSut(
			vNodeOptions: [
				new($"{SectionName}:Archive:Enabled", "true"),
			]);

		Assert.False(sut.MergeChunks);
	}

	[Theory]
	[InlineData(null, 0)]
	[InlineData(-5, -5)]
	[InlineData(0, 0)]
	[InlineData(5, 5)]
	public void threshold_can_be_set(int? configuredThreshold, int expectedThreshold) {
		var sut = GenSut(threshold: configuredThreshold);

		Assert.Equal(expectedThreshold, sut.ChunkExecutionThreshold);
	}
}
