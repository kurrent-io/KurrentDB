// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Xunit;

namespace EventStore.LogV3.Tests;

public class StreamWriteIdTests {
	[Theory]
	[InlineData(Raw.StreamWriteId.MaxStartingEventNumber + 2)]
	[InlineData(-1)]
	public void cant_set_starting_event_number_out_of_range(long x) {
		var writeId = new Raw.StreamWriteId {
			ParentTopicNumber = 15,
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => {
			writeId.StartingEventNumber = x;
		});
		Assert.Equal(15, writeId.ParentTopicNumber);
	}

	[Theory]
	[InlineData(Raw.StreamWriteId.EventNumberDeletedStream)]
	[InlineData(Raw.StreamWriteId.MaxStartingEventNumber)]
	[InlineData(0)]
	public void can_set_starting_event_number(long x) {
		var writeId = new Raw.StreamWriteId {
			StartingEventNumber = 123,
			ParentTopicNumber = 15,
		};

		writeId.StartingEventNumber = x;

		Assert.Equal(15, writeId.ParentTopicNumber);
		Assert.Equal(x, writeId.StartingEventNumber);
	}

	[Fact]
	public void can_get_starting_event_number() {
		var writeId = new Raw.StreamWriteId {
			StartingEventNumber = Raw.StreamWriteId.MaxStartingEventNumber,
		};

		writeId.ParentTopicNumber = 15;

		Assert.Equal(15, writeId.ParentTopicNumber);
		Assert.Equal(Raw.StreamWriteId.MaxStartingEventNumber, writeId.StartingEventNumber);
	}
}
