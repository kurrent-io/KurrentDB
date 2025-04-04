// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.PersistentSubscription;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class ReadBatchSizeTests {
	[TestCase(1, 1)]
	[TestCase(2, 1)]
	public void read_batch_size_greater_or_equal_to_buffer_size_throws(int readBatchSize, int bufferSize) {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new Core.Services.PersistentSubscription.PersistentSubscription(PersistentSubscriptionToStreamParamsBuilder
				.CreateFor("stream", "group")
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(new FakeCheckpointReader())
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent()
				.WithReadBatchOf(readBatchSize)
				.WithHistoryBufferSizeOf(bufferSize)));
	}
}
