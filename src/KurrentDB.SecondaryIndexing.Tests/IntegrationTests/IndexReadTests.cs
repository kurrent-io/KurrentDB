// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using Xunit.Abstractions;
using Position = KurrentDB.Core.Services.Transport.Common.Position;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginEnabled")]
public class IndexReadTests(
	SecondaryIndexingEnabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingTestBase(fixture, output) {
	private readonly string[] _expectedEventData = ["""{"test":"123"}""", """{"test":"321"}"""];

	[Fact]
	public async Task Appended_events_are_indexed() {
		var streamName = RandomStreamName();
		var appendResult = await fixture.AppendToStream(streamName, _expectedEventData);

		string[] indexNames = [
			DefaultIndexConstants.IndexName,
			$"{CategoryIndexConstants.IndexPrefix}{CategoryName}",
			$"{EventTypeIndexConstants.IndexPrefix}test"
		];
		foreach (var name in indexNames) {
			await ValidateRead(streamName, name, appendResult.Position);
		}
	}

	async Task ValidateRead(string streamName, string index, Position position) {
		var readResult = await fixture.ReadUntil(index, position);

		Assert.NotEmpty(readResult);
		var results = readResult.Where(e => e.Event.EventStreamId == streamName).ToList();
		Assert.Equal(_expectedEventData.Length, results.Count);
		Assert.All(results, e => Assert.Contains(Encoding.UTF8.GetString(e.Event.Data.Span), _expectedEventData));
	}
}
