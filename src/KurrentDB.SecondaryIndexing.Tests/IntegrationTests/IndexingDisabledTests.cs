// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class IndexingDisabledTests(
	SecondaryIndexingDisabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingTest<SecondaryIndexingDisabledFixture>(fixture, output), IClassFixture<SecondaryIndexingDisabledFixture> {

	[Fact]
	public async Task Index_streams_should_not_be_found() {
		await Fixture.AppendToStream(RandomStreamName(), """{"test":"123"}""", """{"test":"321"}""");

		await Assert.ThrowsAsync<ReadResponseException.StreamNotFound>(async () =>
			await Fixture.ReadUntil(SystemStreams.DefaultSecondaryIndex, 2, TimeSpan.FromMilliseconds(500))
		);
	}
}
