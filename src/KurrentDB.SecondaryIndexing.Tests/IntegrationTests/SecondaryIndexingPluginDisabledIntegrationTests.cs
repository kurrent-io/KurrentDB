// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Indices.Default;
using KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginDisabled")]
public class SecondaryIndexingPluginDisabledIntegrationTests_LogV2(
	SecondaryIndexingDisabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingPluginDisabledIntegrationTests<string>(fixture, output);

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginDisabled")]
public class SecondaryIndexingPluginDisabledIntegrationTests_LogV3(
	SecondaryIndexingDisabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingPluginDisabledIntegrationTests<string>(fixture, output);

public abstract class SecondaryIndexingPluginDisabledIntegrationTests<TStreamId>(
	SecondaryIndexingDisabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingPluginIntegrationTest(fixture, output) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task IndexStreamIsNotSetUp_ForDisabledPlugin() {
		var result = await fixture.AppendToStream(RandomStreamName(), _expectedEventData);

		await Assert.ThrowsAsync<ReadResponseException.StreamNotFound>(async () =>
			await fixture.ReadUntil(DefaultIndexConstants.IndexName, result.Position, TimeSpan.FromMilliseconds(250))
		);
	}
}
