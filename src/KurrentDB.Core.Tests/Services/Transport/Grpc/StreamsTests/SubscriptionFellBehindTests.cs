// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Services.Transport.Grpc;
using NUnit.Framework;
using GrpcMetadata = KurrentDB.Core.Services.Transport.Grpc.Constants.Metadata;
using Position = KurrentDB.Core.Services.Transport.Common.Position;

namespace KurrentDB.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class SubscriptionFellBehindTests {
	public abstract class when_a_live_subscription_falls_behind<TLogFormat, TStreamId>(uint compatibility)
		: GrpcSpecification<TLogFormat, TStreamId> {

		private const string FinishEventType = nameof(FinishEventType);
		private const int NumExistingEvents = 10;

		// each batch is committed at once, so its events reach the subscription much faster than they
		// can be forwarded to the client, which overflows the live buffer of the subscription.
		private const int NumBatchesToFallBehind = 2;
		private const int NumEventsPerBatch = 500;

		protected readonly string StreamName = $"stream-{Uuid.NewUuid()}";
		private protected readonly List<ReadResp> Responses = [];

		private bool ExpectFellBehind => compatibility >= ResponseConverter.FellBehindCompatibility;

		private protected abstract void SubscribeTo(ReadReq.Types.Options options);

		protected override Task Given() => Append(CreateEvents(NumExistingEvents));

		protected override async Task When() {
			var options = new ReadReq.Types.Options {
				Subscription = new(),
				ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
				UuidOption = new() { Structured = new() },
				ControlOption = new() { Compatibility = compatibility },
			};
			SubscribeTo(options);

			// ends the call if the subscription does not fall behind, before the fixture gives up waiting for it
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
			using var call = StreamsClient.Read(new() { Options = options }, GetCallOptions(AdminCredentials));

			// go live
			while (await call.ResponseStream.MoveNext(cts.Token)) {
				Responses.Add(call.ResponseStream.Current);
				if (call.ResponseStream.Current.ContentCase == ReadResp.ContentOneofCase.CaughtUp)
					break;
			}

			// fall behind
			for (var i = 0; i < NumBatchesToFallBehind; i++)
				await Append(CreateEvents(NumEventsPerBatch));

			await Append([CreateEvent(FinishEventType)]);

			// the subscription only catches up a second time if it fell behind. depending on how quickly it
			// does so, the last event is received either while catching up or once live again.
			var finished = false;
			var caughtUpAgain = false;
			while (!(finished && caughtUpAgain) && await call.ResponseStream.MoveNext(cts.Token)) {
				var response = call.ResponseStream.Current;
				Responses.Add(response);

				if (response.ContentCase == ReadResp.ContentOneofCase.Event &&
				    response.Event.Event.Metadata[GrpcMetadata.Type] == FinishEventType)
					finished = true;
				else if (response.ContentCase == ReadResp.ContentOneofCase.CaughtUp)
					caughtUpAgain = true;
			}
		}

		private async Task Append(IEnumerable<BatchAppendReq.Types.ProposedMessage> events) {
			var response = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) },
					Any = new(),
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { events }
			});

			Assert.AreEqual(BatchAppendResp.ResultOneofCase.Success, response.ResultCase);
		}

		private IEnumerable<ReadResp> EventsOfTheStream => Responses.Where(x =>
			x.ContentCase == ReadResp.ContentOneofCase.Event &&
			x.Event.Event.StreamIdentifier.StreamName.ToStringUtf8() == StreamName);

		private int Count(ReadResp.ContentOneofCase contentCase) => Responses.Count(x => x.ContentCase == contentCase);

		// the checkpoint that the subscription has reached after having sent the first `count` responses
		protected abstract (long? StreamRevision, Position? Position) CheckpointAfter(int count);

		protected long? LastStreamRevisionIn(int count) => Responses
			.Take(count)
			.LastOrDefault(x => x.ContentCase == ReadResp.ContentOneofCase.Event)
			?.Event.Event.StreamRevision is { } revision ? (long)revision : null;

		protected Position? LastPositionIn(int count) => Responses
			.Take(count)
			.Select(x => x.ContentCase switch {
				ReadResp.ContentOneofCase.Event => new Position(x.Event.Event.CommitPosition, x.Event.Event.PreparePosition),
				ReadResp.ContentOneofCase.Checkpoint => new Position(x.Checkpoint.CommitPosition, x.Checkpoint.PreparePosition),
				_ => (Position?)null
			})
			.Where(x => x.HasValue)
			.Max();

		[Test]
		public void receives_all_the_events_in_order() {
			var expected = NumExistingEvents + NumBatchesToFallBehind * NumEventsPerBatch + 1;
			CollectionAssert.AreEqual(
				Enumerable.Range(0, expected).Select(x => (ulong)x),
				EventsOfTheStream.Select(x => x.Event.Event.StreamRevision));
		}

		[Test]
		public void catches_up_again_after_falling_behind() {
			Assert.GreaterOrEqual(Count(ReadResp.ContentOneofCase.CaughtUp), 2);
		}

		[Test]
		public void receives_fell_behind_only_when_compatible() {
			if (ExpectFellBehind) {
				// goes live once, and once more after each time it falls behind
				Assert.AreEqual(Count(ReadResp.ContentOneofCase.CaughtUp) - 1, Count(ReadResp.ContentOneofCase.FellBehind));
			} else {
				Assert.Zero(Count(ReadResp.ContentOneofCase.FellBehind));
			}
		}

		[Test]
		public void fell_behind_is_followed_by_caught_up() {
			if (!ExpectFellBehind)
				return;

			var live = false;
			foreach (var response in Responses) {
				switch (response.ContentCase) {
					case ReadResp.ContentOneofCase.CaughtUp:
						Assert.False(live, "caught up when already live");
						live = true;
						break;
					case ReadResp.ContentOneofCase.FellBehind:
						Assert.True(live, "fell behind when not live");
						live = false;
						break;
				}
			}

			Assert.True(live);
		}

		[Test]
		public void fell_behind_and_caught_up_have_the_checkpoint_to_resume_from() {
			for (var i = 0; i < Responses.Count; i++) {
				var response = Responses[i];
				var (timestamp, streamRevision, position) = response.ContentCase switch {
					ReadResp.ContentOneofCase.CaughtUp => (
						response.CaughtUp.Timestamp,
						response.CaughtUp.HasStreamRevision ? response.CaughtUp.StreamRevision : (long?)null,
						response.CaughtUp.Position),
					ReadResp.ContentOneofCase.FellBehind => (
						response.FellBehind.Timestamp,
						response.FellBehind.HasStreamRevision ? response.FellBehind.StreamRevision : (long?)null,
						response.FellBehind.Position),
					_ => default
				};

				if (timestamp is null)
					continue;

				var expected = CheckpointAfter(i);
				Assert.AreEqual(expected.StreamRevision, streamRevision, $"{response.ContentCase} at {i}");
				Assert.AreEqual(
					expected.Position,
					position is null ? null : new Position(position.CommitPosition, position.PreparePosition),
					$"{response.ContentCase} at {i}");
			}
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), 0u)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), ResponseConverter.FellBehindCompatibility)]
	public class when_a_live_subscription_to_all_falls_behind<TLogFormat, TStreamId>(uint compatibility)
		: when_a_live_subscription_falls_behind<TLogFormat, TStreamId>(compatibility) {

		private protected override void SubscribeTo(ReadReq.Types.Options options) {
			options.All = new() { Start = new() };
			options.NoFilter = new();
		}

		protected override (long? StreamRevision, Position? Position) CheckpointAfter(int count) =>
			(null, LastPositionIn(count));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), 0u)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), ResponseConverter.FellBehindCompatibility)]
	public class when_a_live_filtered_subscription_to_all_falls_behind<TLogFormat, TStreamId>(uint compatibility)
		: when_a_live_subscription_falls_behind<TLogFormat, TStreamId>(compatibility) {

		private protected override void SubscribeTo(ReadReq.Types.Options options) {
			options.All = new() { Start = new() };
			options.Filter = new() {
				Count = new Empty(),
				CheckpointIntervalMultiplier = 1,
				StreamIdentifier = new() { Prefix = { StreamName } },
			};
		}

		protected override (long? StreamRevision, Position? Position) CheckpointAfter(int count) =>
			(null, LastPositionIn(count));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string), 0u)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), ResponseConverter.FellBehindCompatibility)]
	public class when_a_live_subscription_to_a_stream_falls_behind<TLogFormat, TStreamId>(uint compatibility)
		: when_a_live_subscription_falls_behind<TLogFormat, TStreamId>(compatibility) {

		private protected override void SubscribeTo(ReadReq.Types.Options options) {
			options.Stream = new() {
				Start = new(),
				StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) },
			};
			options.NoFilter = new();
		}

		protected override (long? StreamRevision, Position? Position) CheckpointAfter(int count) =>
			(LastStreamRevisionIn(count), null);
	}
}
