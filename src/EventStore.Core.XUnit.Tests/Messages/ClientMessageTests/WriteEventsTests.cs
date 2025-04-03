// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Core.Messages;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Messages.ClientMessageTests;

public class WriteEventsTests {
	static ClientMessage.WriteEvents CreateSut(
		string eventStreamId = default,
		long expectedVersion = default) =>

		new(internalCorrId: Guid.NewGuid(),
			correlationId: Guid.NewGuid(),
			envelope: new NoopEnvelope(),
			requireLeader: false,
			eventStreamId: eventStreamId,
			expectedVersion: expectedVersion,
			events: [],
			user: default);

	[Theory]
	[InlineData("normal")]
	[InlineData("  not normal  ")] // doubtful that this is a good idea. would be a breaking change to reject though
	[InlineData("$$normal")]
	public void accepts_valid_stream_ids(string streamId) {
		CreateSut(eventStreamId: streamId);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("$$")] // "" is an invalid name, and so metadata cannot be set for it
	public void rejects_invalid_stream_ids(string streamId) {
		var ex = Assert.Throws<ArgumentOutOfRangeException>(() => {
			CreateSut(eventStreamId: streamId);
		});

		Assert.Contains("eventStreamId", ex.Message);
	}
}
