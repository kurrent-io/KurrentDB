// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginEnabled")]
public class SecondaryIndexingPluginEnabledIntegrationTests(
	SecondaryIndexingEnabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingPluginIntegrationTest(fixture, output) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task ReadsIndexStream_ForEnabledPlugin() {
		var appendResult = await fixture.AppendToStream(RandomStreamName(), _expectedEventData);

		var readResult = await fixture.ReadUntil(DefaultIndexConstants.IndexName, appendResult.Position);

		Assert.NotEmpty(readResult);
	}
}
