// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using KurrentDB.Core.Services.Transport.Enumerators;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class ResponseConverterTests {
	static readonly DateTime TimeStamp = new(2025, 04, 17, 06, 30, 30, DateTimeKind.Utc);

	[Fact]
	public void can_convert_checkpoint_received() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.CheckpointReceived(
				timestamp: TimeStamp,
				commitPosition: 100,
				preparePosition: 50),
			uuidOption: null,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.Checkpoint, actual.ContentCase);

		Assert.Equal(TimeStamp, actual.Checkpoint.Timestamp.ToDateTime());

		Assert.Equal(100ul, actual.Checkpoint.CommitPosition);
		Assert.Equal(50ul, actual.Checkpoint.PreparePosition);
	}

	[Fact]
	public void subscription_caught_up_has_timestamp() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionCaughtUp(
				timestamp: TimeStamp,
				allCheckpoint: new Data.TFPos(100, 50)),
			uuidOption: null,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);

		Assert.Equal(TimeStamp, actual.CaughtUp.Timestamp.ToDateTime());
	}

	[Fact]
	public void subscription_caught_up_supports_all_checkpoint() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionCaughtUp(
				timestamp: TimeStamp,
				allCheckpoint: new Data.TFPos(100, 50)),
			uuidOption: null,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);

		Assert.False(actual.CaughtUp.HasStreamRevision);

		Assert.Equal(100ul, actual.CaughtUp.Position.CommitPosition);
		Assert.Equal(50ul, actual.CaughtUp.Position.PreparePosition);
	}

	[Fact]
	public void subscription_caught_up_supports_stream_checkpoint() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionCaughtUp(
				timestamp: TimeStamp,
				streamCheckpoint: 5),
			uuidOption: null,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);

		Assert.True(actual.CaughtUp.HasStreamRevision);
		Assert.Equal(5, actual.CaughtUp.StreamRevision);

		Assert.Null(actual.CaughtUp.Position);
	}
}
