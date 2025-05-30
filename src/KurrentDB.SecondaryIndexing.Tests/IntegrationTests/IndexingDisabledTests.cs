// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginDisabled")]
public class IndexingDisabledTests(
	SecondaryIndexingDisabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingTestBase(fixture, output) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task Index_streams_should_not_be_found() {
		var result = await fixture.AppendToStream(RandomStreamName(), _expectedEventData);

		await Assert.ThrowsAsync<ReadResponseException.StreamNotFound>(async () =>
			await fixture.ReadUntil(DefaultIndexConstants.IndexName, result.Position, TimeSpan.FromMilliseconds(500))
		);
	}
}
