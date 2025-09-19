// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Storage.InMemory;

public class VirtualStreamReaderTests {
	private readonly VirtualStreamReader _sut;
	private readonly NodeStateListenerService _listener;

	public VirtualStreamReaderTests() {
		var channel = Channel.CreateUnbounded<Message>();
		_listener = new NodeStateListenerService(
			new EnvelopePublisher(new ChannelEnvelope(channel)),
			new InMemoryLog());
		_sut = new VirtualStreamReader([_listener.Stream]);
	}

	private static ClientMessage.ReadStreamEventsBackward GenReadBackwards(Guid correlation, long fromEventNumber, int maxCount, string eventStreamId = SystemStreams.NodeStateStream) {
		return new ClientMessage.ReadStreamEventsBackward(
			internalCorrId: correlation,
			correlationId: correlation,
			envelope: new NoopEnvelope(),
			eventStreamId,
			fromEventNumber: fromEventNumber,
			maxCount: maxCount,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: ClaimsPrincipal.Current,
			replyOnExpired: false);
	}

	public static ClientMessage.ReadStreamEventsForward GenReadForwards(Guid correlation, long fromEventNumber, int maxCount, string eventStreamId = SystemStreams.NodeStateStream) {
		return new ClientMessage.ReadStreamEventsForward(
			internalCorrId: correlation,
			correlationId: correlation,
			envelope: new NoopEnvelope(),
			eventStreamId,
			fromEventNumber: fromEventNumber,
			maxCount: maxCount,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: ClaimsPrincipal.Current,
			replyOnExpired: true);
	}

	public class ReadForwardEmptyTests : VirtualStreamReaderTests {
		[Fact]
		public async Task read_forwards_empty() {
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}
	}

	public class ReadForwardTests : VirtualStreamReaderTests {
		[Fact]
		public async Task read_forwards() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(1, result.NextEventNumber);
			Assert.Equal(0, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(0, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_forwards_beyond_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 1000, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(1_000, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(1, result.NextEventNumber);
			Assert.Equal(0, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}

		[Fact]
		public async Task read_forwards_below_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(2, result.NextEventNumber);
			Assert.Equal(1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(1, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_forwards_far_below_latest_event() {
			// we specify maxCount, not an upper event number, so it is acceptable in this case to either
			// - find event 49 (like we do for regular stream forward maxAge reads if old events have been scavenged)
			//   and reach the end of the stream (nextEventNumber == 50)
			// - not find event 49 (like we do for regular maxCount reads, even if old events have been scavenged)
			//   and not reach the end of the stream (nextEventNumber <= 49 so that we can read it in subsequent pages)
			// current implementation finds the event.
			for (var i = 0; i < 50; i++)
				_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(50, result.NextEventNumber);
			Assert.Equal(49, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(49, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_forwards_with_more_than_one_matching_owner_uses_first_registered() {
			var sut = new VirtualStreamReader([
				new DummyVirtualStreamReader(SystemStreams.NodeStateStream),
				_listener.Stream
			]);

			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			// Ensure that DummyVirtualStreamReader was used instead of the proper listener
			Assert.Equal(DummyVirtualStreamReader.DummyEventNumber, result.NextEventNumber);
		}
	}

	public class ReadForwardNoMatchingReaderTests : VirtualStreamReaderTests {
		private const string StreamIdWithoutReader = "I don't have owner";

		[Fact]
		public async Task read_forwards_no_matching_reader() {
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10, eventStreamId: StreamIdWithoutReader), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(StreamIdWithoutReader, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}

		[Fact]
		public async Task read_forwards_no_readers() {
			var sut = new VirtualStreamReader([]);
			var correlation = Guid.NewGuid();

			var result = await sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10, eventStreamId: StreamIdWithoutReader), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(StreamIdWithoutReader, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}
	}

	public class ReadBackwardsEmptyTests : VirtualStreamReaderTests {
		[Fact]
		public async Task read_backwards_empty() {
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}
	}

	public class ReadBackwardsTests : VirtualStreamReaderTests {
		[Fact]
		public async Task read_backwards() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(0, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(0, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_backwards_beyond_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 5, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(5, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(0, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(0, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_backwards_far_beyond_latest_event() {
			// we specify maxCount, not a lower event number, so it is acceptable in this case to either
			// - find event 0 (like we do for regular stream forward maxAge reads if old events have been scavenged)
			//   and reach the end of the stream (nextEventNumber == -1)
			// - not find event 0 (like we do for regular maxCount reads, even if old events have been scavenged)
			//   and not reach the end of the stream (nextEventNumber >= 0 so that we can read it in subsequent pages)
			// current implementation finds the event.
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 1000, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(1_000, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(0, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Single(result.Events);

			var @event = result.Events[0];
			Assert.Equal(0, @event.Event.EventNumber);
			Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
			Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		}

		[Fact]
		public async Task read_backwards_below_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.Success, result.Result);
			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}

		[Fact]
		public async Task read_backwards_with_more_than_one_matching_owner_uses_first_registered() {
			var sut = new VirtualStreamReader([
				new DummyVirtualStreamReader(SystemStreams.NodeStateStream),
				_listener.Stream
			]);

			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = await sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10), CancellationToken.None);

			Assert.Equal(SystemStreams.NodeStateStream, result.EventStreamId);
			// Ensure that DummyVirtualStreamReader was used instead of the proper listener
			Assert.Equal(DummyVirtualStreamReader.DummyEventNumber, result.NextEventNumber);
		}
	}

	public class ReadBackwardsNoMatchingReaderTests : VirtualStreamReaderTests {
		private const string StreamIdWithoutReader = "I don't have owner";

		[Fact]
		public async Task read_backwards_no_matching_reader() {
			var correlation = Guid.NewGuid();

			var result = await _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10, eventStreamId: StreamIdWithoutReader), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(StreamIdWithoutReader, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}

		[Fact]
		public async Task read_backwards_no_readers() {
			var sut = new VirtualStreamReader([]);
			var correlation = Guid.NewGuid();

			var result = await sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10, eventStreamId: StreamIdWithoutReader), CancellationToken.None);

			Assert.Equal(correlation, result.CorrelationId);
			Assert.Equal(ReadStreamResult.NoStream, result.Result);
			Assert.Equal(StreamIdWithoutReader, result.EventStreamId);
			Assert.Equal(0, result.FromEventNumber);
			Assert.Equal(10, result.MaxCount);
			Assert.Equal(-1, result.NextEventNumber);
			Assert.Equal(-1, result.LastEventNumber);
			Assert.True(result.IsEndOfStream);
			Assert.Empty(result.Events);
		}
	}

	class DummyVirtualStreamReader(string ownedStreamId) : IVirtualStreamReader {
		public const long DummyEventNumber = 123;
		public const long DummyIndexPosition = 234;

		public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
			ClientMessage.ReadStreamEventsForward msg,
			CancellationToken token
		) =>
			ValueTask.FromResult(new ClientMessage.ReadStreamEventsForwardCompleted(
				msg.CorrelationId,
				msg.EventStreamId,
				msg.FromEventNumber,
				msg.MaxCount,
				ReadStreamResult.Success,
				[],
				StreamMetadata.Empty,
				isCachePublic: false,
				error: string.Empty,
				nextEventNumber: DummyEventNumber,
				lastEventNumber: DummyEventNumber,
				isEndOfStream: true,
				tfLastCommitPosition: DummyIndexPosition
			));

		public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
			ClientMessage.ReadStreamEventsBackward msg,
			CancellationToken token
		) =>
			ValueTask.FromResult(new ClientMessage.ReadStreamEventsBackwardCompleted(
				msg.CorrelationId,
				msg.EventStreamId,
				msg.FromEventNumber,
				msg.MaxCount,
				ReadStreamResult.Success,
				[],
				streamMetadata: StreamMetadata.Empty,
				isCachePublic: false,
				error: string.Empty,
				nextEventNumber: DummyEventNumber,
				lastEventNumber: DummyEventNumber,
				isEndOfStream: true,
				tfLastCommitPosition: DummyIndexPosition
			));

		public long GetLastEventNumber(string streamId) => 123;

		public long GetLastIndexedPosition(string streamId) => 123;

		public bool CanReadStream(string streamId) => streamId == ownedStreamId;
	}
}
