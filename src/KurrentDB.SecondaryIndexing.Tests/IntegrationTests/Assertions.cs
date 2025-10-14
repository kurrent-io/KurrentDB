// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public partial class IndexingTests {
	private static void AssertResolvedEventsMatch(List<ResolvedEvent> results, ResolvedEvent[] expectedRecords, bool forwards) {
		Assert.NotEmpty(results);
		Assert.Equal(expectedRecords.Length, results.Count);

		Assert.All(results,
			(item, index) => {
				Assert.NotEqual(0L, item.Event.LogPosition);
				Assert.NotEqual(0L, item.Event.TransactionPosition);

				if (index == 0)
					return;

				var previousItem = results[index - 1];

				if (forwards) {
					Assert.True(item.Event.LogPosition > previousItem.Event.LogPosition, $"{item.Event.LogPosition} should be greater than {previousItem.Event.LogPosition}");
				} else {
					Assert.True(item.Event.LogPosition < previousItem.Event.LogPosition, $"{item.Event.LogPosition} should be less than {previousItem.Event.LogPosition}");
				}
			});

		Assert.All(results, item => Assert.NotEqual(default, item.Event.TimeStamp));

		for (var sequence = 0; sequence < results.Count; sequence++) {
			var actual = results[sequence];
			var expected = expectedRecords[sequence];

			Assert.Equal(expected.Event.EventId, actual.Event.EventId);
			Assert.Equal(expected.Event.EventType, actual.Event.EventType);
			Assert.Equal(expected.Event.Data, actual.Event.Data);
			Assert.Equal(expected.Event.EventNumber, actual.Event.EventNumber);

			Assert.NotEqual(default, actual.Event.Flags);
			Assert.Equal(expected.Event.Metadata, actual.Event.Metadata);
			Assert.Equal(actual.Event.TransactionOffset, actual.Event.TransactionOffset);
			Assert.Equal(ReadEventResult.Success, actual.ResolveResult);
		}
	}
}
