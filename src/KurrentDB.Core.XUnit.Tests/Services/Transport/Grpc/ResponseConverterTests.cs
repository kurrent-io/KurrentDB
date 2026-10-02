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
			compatibility: 0,
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
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: 0,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);

		Assert.Equal(TimeStamp, actual.CaughtUp.Timestamp.ToDateTime());
	}

	[Fact]
	public void subscription_caught_up_supports_all_checkpoint() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionCaughtUp(
				timestamp: TimeStamp,
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: 0,
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
			compatibility: 0,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);

		Assert.True(actual.CaughtUp.HasStreamRevision);
		Assert.Equal(5, actual.CaughtUp.StreamRevision);

		Assert.Null(actual.CaughtUp.Position);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(1u)]
	public void subscription_fell_behind_is_not_sent_below_its_compatibility_level(uint compatibility) {
		Assert.False(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: compatibility,
			out var actual));

		Assert.Null(actual);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(ResponseConverter.FellBehindCompatibility)]
	public void subscription_caught_up_is_sent_at_any_compatibility_level(uint compatibility) {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionCaughtUp(
				timestamp: TimeStamp,
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: compatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.CaughtUp, actual.ContentCase);
	}

	[Fact]
	public void subscription_fell_behind_has_timestamp() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: ResponseConverter.FellBehindCompatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.FellBehind, actual.ContentCase);

		Assert.Equal(TimeStamp, actual.FellBehind.Timestamp.ToDateTime());
	}

	[Fact]
	public void subscription_fell_behind_supports_all_checkpoint() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				allCheckpoint: new Core.Data.TFPos(100, 50)),
			uuidOption: null,
			compatibility: ResponseConverter.FellBehindCompatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.FellBehind, actual.ContentCase);

		Assert.False(actual.FellBehind.HasStreamRevision);

		Assert.Equal(100ul, actual.FellBehind.Position.CommitPosition);
		Assert.Equal(50ul, actual.FellBehind.Position.PreparePosition);
	}

	[Fact]
	public void subscription_fell_behind_supports_stream_checkpoint() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				streamCheckpoint: 5),
			uuidOption: null,
			compatibility: ResponseConverter.FellBehindCompatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.FellBehind, actual.ContentCase);

		Assert.True(actual.FellBehind.HasStreamRevision);
		Assert.Equal(5, actual.FellBehind.StreamRevision);

		Assert.Null(actual.FellBehind.Position);
	}

	[Fact]
	public void subscription_fell_behind_has_no_checkpoint_when_nothing_was_sent_from_the_stream() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				streamCheckpoint: -1),
			uuidOption: null,
			compatibility: ResponseConverter.FellBehindCompatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.FellBehind, actual.ContentCase);

		Assert.False(actual.FellBehind.HasStreamRevision);
		Assert.Null(actual.FellBehind.Position);
	}

	[Fact]
	public void subscription_fell_behind_has_no_checkpoint_when_nothing_was_sent_from_all() {
		Assert.True(ResponseConverter.TryConvertReadResponse(
			new ReadResponse.SubscriptionFellBehind(
				timestamp: TimeStamp,
				allCheckpoint: Core.Data.TFPos.HeadOfTf),
			uuidOption: null,
			compatibility: ResponseConverter.FellBehindCompatibility,
			out var actual));

		Assert.Equal(ReadResp.ContentOneofCase.FellBehind, actual.ContentCase);

		Assert.False(actual.FellBehind.HasStreamRevision);
		Assert.Null(actual.FellBehind.Position);
	}
}
