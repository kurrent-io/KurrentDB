// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
#pragma warning disable CA1861 // Avoid constant arrays as arguments
#pragma warning disable xUnit2023 // Do not use collection methods for single-item collections

using System;
using System.Net;
using Grpc.Core;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Services.Transport.Grpc.V2;
using KurrentDB.Protocol.V2;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc.V2;

public class MSAResponseConverterTests {
	const int TestChunkSize = 10_000;
	const int TestMaxAppendSize = 400;
	const int TestMaxAppendEventSize = 300;

	MultiStreamAppendConverter Sut { get; } = new(TestChunkSize, TestMaxAppendSize, TestMaxAppendEventSize);

	[Fact]
	public void converts_when_first_expected_version_is_wrong() {
		// given
		var requests = new AppendStreamRequest[] {
			new() { Stream = "stream-at-index-0" },
			new() { Stream = "stream-at-index-1" },
			new() { Stream = "stream-at-index-2" },
			new() { Stream = "stream-at-index-3" },
		};

		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.WrongExpectedVersion,
			message: "the details",
			failureStreamIndexes: new[] { 0 },
			failureCurrentVersions: new long[] { 10 });

		// when
		var result = Sut.ConvertToResponse(input, requests);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal("stream-at-index-0", x.Stream);
				Assert.Equal(10, x.StreamRevisionConflict.StreamRevision);
			});
	}

	[Fact]
	public void converts_when_second_expected_version_is_wrong() {
		// given
		var requests = new AppendStreamRequest[] {
			new() { Stream = "stream-at-index-0" },
			new() { Stream = "stream-at-index-1" },
			new() { Stream = "stream-at-index-2" },
			new() { Stream = "stream-at-index-3" },
		};

		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.WrongExpectedVersion,
			message: "the details",
			failureStreamIndexes: new[] { 1 },
			failureCurrentVersions: new long[] { 11 });

		// when
		var result = Sut.ConvertToResponse(input, requests);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal("stream-at-index-1", x.Stream);
				Assert.Equal(11, x.StreamRevisionConflict.StreamRevision);
			});
	}

	[Fact]
	public void converts_when_multiple_expected_versions_are_wrong() {
		// given
		var requests = new AppendStreamRequest[] {
			new() { Stream = "stream-at-index-0" },
			new() { Stream = "stream-at-index-1" },
			new() { Stream = "stream-at-index-2" },
			new() { Stream = "stream-at-index-3" },
		};

		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.WrongExpectedVersion,
			message: "the details",
			failureStreamIndexes: new[] { 1, 3 },
			failureCurrentVersions: new long[] { 11, 13 });

		// when
		var result = Sut.ConvertToResponse(input, requests);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal("stream-at-index-1", x.Stream);
				Assert.Equal(11, x.StreamRevisionConflict.StreamRevision);
			},
			x => {
				Assert.Equal("stream-at-index-3", x.Stream);
				Assert.Equal(13, x.StreamRevisionConflict.StreamRevision);
			});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void converts_when_stream_is_deleted(bool isStreamKnown) {
		// given
		var requests = new AppendStreamRequest[] {
			new() { Stream = "stream-at-index-0" },
			new() { Stream = "stream-at-index-1" },
			new() { Stream = "stream-at-index-2" },
			new() { Stream = "stream-at-index-3" },
		};

		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.StreamDeleted,
			message: "the details",
			failureStreamIndexes: isStreamKnown ? [1] : [],
			failureCurrentVersions: isStreamKnown ? [11] : []);

		// when
		var result = Sut.ConvertToResponse(input, requests);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal(isStreamKnown ? "stream-at-index-1" : "<unknown>", x.Stream);
				Assert.Equal(AppendStreamFailure.ErrorOneofCase.StreamDeleted, x.ErrorCase);
			});
	}

	[Fact]
	public void converts_when_access_is_denied() {
		// suspect this cannot happen on the gRPC interfaces but cover anyway
		// given
		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.AccessDenied,
			message: "the details");

		// when
		var result = Sut.ConvertToResponse(input, []);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal("<unknown>", x.Stream);
			});
	}

	[Fact]
	public void converts_when_invalid_transaction() {
		// transaction is too big for any chunk
		// given
		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: OperationResult.InvalidTransaction,
			message: "the details");

		// when
		var result = Sut.ConvertToResponse(input, []);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal(TestChunkSize, x.TransactionMaxSizeExceeded.MaxSize);
			});
	}

	[Fact]
	public void throws_when_not_handled_because_not_ready() {
		// given
		var input = new ClientMessage.NotHandled(
			Guid.NewGuid(),
			ClientMessage.NotHandled.Types.NotHandledReason.NotReady,
			"not ready");

		// when
		var ex = Assert.Throws<RpcException>(() =>
			 Sut.ConvertToResponse(input, []));

		// then
		Assert.Equal("Server Is Not Ready", ex.Status.Detail);
		Assert.Equal(StatusCode.Unavailable, ex.Status.StatusCode);
	}

	[Fact]
	public void throws_when_not_handled_because_not_leader() {
		// given
		var input = new ClientMessage.NotHandled(
			Guid.NewGuid(),
			ClientMessage.NotHandled.Types.NotHandledReason.NotLeader,
			new ClientMessage.NotHandled.Types.LeaderInfo(
				externalTcp: new DnsEndPoint("node3", 1113),
				isSecure: true,
				http: new DnsEndPoint("node3", 2113)));

		// when
		var ex = Assert.Throws<RpcException>(() =>
			 Sut.ConvertToResponse(input, []));

		// then
		Assert.Equal("Leader info available", ex.Status.Detail);
		Assert.Equal(StatusCode.NotFound, ex.Status.StatusCode);
		Assert.Collection(
			ex.Trailers,
			x => {
				Assert.Equal(Constants.Exceptions.ExceptionKey, x.Key);
				Assert.Equal(Constants.Exceptions.NotLeader, x.Value);
			},
			x => {
				Assert.Equal(Constants.Exceptions.LeaderEndpointHost, x.Key);
				Assert.Equal("node3", x.Value);
			},
			x => {
				Assert.Equal(Constants.Exceptions.LeaderEndpointPort, x.Key);
				Assert.Equal("2113", x.Value);
			});
	}

	[Fact]
	public void throws_when_unexpected_response() {
		// given
		var input = new ClientMessage.CheckpointReached(
			correlationId: Guid.NewGuid(),
			position: null);

		// when
		var ex = Assert.Throws<RpcException>(() =>
			 Sut.ConvertToResponse(input, []));

		// then
		Assert.Equal(StatusCode.Unknown, ex.Status.StatusCode);
	}

	[Theory]
	[InlineData(OperationResult.CommitTimeout, StatusCode.Aborted, "Operation timed out: the details")]
	[InlineData(OperationResult.ForwardTimeout, StatusCode.Aborted, "Operation timed out: the details")]
	[InlineData(OperationResult.PrepareTimeout, StatusCode.Aborted, "Operation timed out: the details")]
	[InlineData((OperationResult)9999, StatusCode.Unknown, "Unexpected OperationResult: 9999")]
	public void throws_when_operation_result_is_exceptional(
		OperationResult exceptionalResult,
		StatusCode expectedStatusCode,
		string expectedDetail) {

		// given
		var input = new ClientMessage.WriteEventsCompleted(
			correlationId: Guid.NewGuid(),
			result: exceptionalResult,
			message: "the details");

		// when
		var ex = Assert.Throws<RpcException>(() => Sut.ConvertToResponse(input, []));

		// then
		Assert.Equal(expectedDetail, ex.Status.Detail);
		Assert.Equal(expectedStatusCode, ex.Status.StatusCode);
	}
}
