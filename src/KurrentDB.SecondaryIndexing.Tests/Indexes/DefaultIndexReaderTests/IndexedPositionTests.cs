// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public class IndexedPositionTests : IndexTestBase {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void WhenIndexedRecords_ReturnsProcessorLastIndexedPosition(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);
		var streamId = Guid.NewGuid().ToString(); // this can be anything for a Default Index, as it's ignored

		// When
		var result = Sut.GetLastIndexedPosition(streamId);

		// Then
		Assert.Equal(300L, result.PreparePosition);
	}

	[Fact]
	public void WhenNoRecords_ReturnsProcessorLastIndexedPosition() {
		// Given -- no records
		var streamId = Guid.NewGuid().ToString(); // this can be anything for a Default Index, as it's ignored

		// When
		var result = Sut.GetLastIndexedPosition(streamId);

		// Then
		Assert.Equal(-1L, result.PreparePosition);
	}
}
