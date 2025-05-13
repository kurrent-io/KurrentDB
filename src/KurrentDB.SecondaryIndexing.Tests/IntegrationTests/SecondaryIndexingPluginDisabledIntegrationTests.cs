// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class SecondaryIndexingPluginDisabledIntegrationTests_LogV2(
	ITestOutputHelper output,
	SecondaryIndexingDisabledAssemblyFixture fixture
) : SecondaryIndexingPluginDisabledIntegrationTests<string>(output, fixture);

[Trait("Category", "Integration")]
public class SecondaryIndexingPluginDisabledIntegrationTests_LogV3(
	ITestOutputHelper output,
	SecondaryIndexingDisabledAssemblyFixture fixture
) : SecondaryIndexingPluginDisabledIntegrationTests<string>(output, fixture);

public abstract class SecondaryIndexingPluginDisabledIntegrationTests<TStreamId>(
	ITestOutputHelper output,
	SecondaryIndexingDisabledAssemblyFixture fixture
) : SecondaryIndexingPluginIntegrationTest(output, fixture) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task NoPluginIsRegistered() {
		var result = await fixture.AppendToStream(RandomStreamName(), _expectedEventData);

		var readEventsFromIndex = await fixture.ReadStream(IndexStreamName).ToListAsync();

		Assert.Empty(readEventsFromIndex);
	}
}

[Trait("Category", "Integration")]
public class SecondaryIndexingPluginEnabledIntegrationTests_LogV2(
	ITestOutputHelper output,
	SecondaryIndexingEnabledAssemblyFixture fixture
) : SecondaryIndexingPluginEnabledIntegrationTests<string>(output, fixture);

[Trait("Category", "Integration")]
public class SecondaryIndexingPluginEnabledIntegrationTests_LogV3(
	ITestOutputHelper output,
	SecondaryIndexingEnabledAssemblyFixture fixture
) : SecondaryIndexingPluginEnabledIntegrationTests<string>(output, fixture);


public abstract class SecondaryIndexingPluginEnabledIntegrationTests<TStreamId>(
	ITestOutputHelper output,
	SecondaryIndexingEnabledAssemblyFixture fixture
) : SecondaryIndexingPluginIntegrationTest(output, fixture) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task NoPluginIsRegistered() {
		var result = await fixture.AppendToStream(RandomStreamName(), _expectedEventData);

		var readEventsFromIndex = await fixture.ReadStream(IndexStreamName).ToListAsync();

		Assert.Empty(readEventsFromIndex);
	}
}
