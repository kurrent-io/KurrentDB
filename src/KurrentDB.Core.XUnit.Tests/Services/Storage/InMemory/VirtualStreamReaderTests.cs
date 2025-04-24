// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Channels;
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

	private static ClientMessage.ReadStreamEventsBackward GenReadBackwards(Guid correlation, long fromEventNumber, int maxCount) {
		return new ClientMessage.ReadStreamEventsBackward(
			internalCorrId: correlation,
			correlationId: correlation,
			envelope: new NoopEnvelope(),
			eventStreamId: SystemStreams.NodeStateStream,
			fromEventNumber: fromEventNumber,
			maxCount: maxCount,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: ClaimsPrincipal.Current);
	}

	public static ClientMessage.ReadStreamEventsForward GenReadForwards(Guid correlation, long fromEventNumber, int maxCount) {
		return new ClientMessage.ReadStreamEventsForward(
			internalCorrId: correlation,
			correlationId: correlation,
			envelope: new NoopEnvelope(),
			eventStreamId: SystemStreams.NodeStateStream,
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
		public void read_forwards_empty() {
			var correlation = Guid.NewGuid();

			var result = _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10));

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
		public void read_forwards() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10));

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
		public void read_forwards_beyond_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 1000, maxCount: 10));

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
		public void read_forwards_below_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10));

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
		public void read_forwards_far_below_latest_event() {
			// we specify maxCount, not an upper event number, so it is acceptable in this case to either
			// - find event 49 (like we do for regular stream forward maxAge reads if old events have been scavenged)
			//   and reach the end of the stream (nextEventNumber == 50)
			// - not find event 49 (like we do for regular maxCount reads, even if old events have been scavenged)
			//   and not reach the end of the stream (nextEventNumber <= 49 so that we can read it in subsequent pages)
			// current implementation finds the event.
			for (var i = 0; i < 50; i++)
				_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadForwards(GenReadForwards(correlation, fromEventNumber: 0, maxCount: 10));

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
	}

	public class ReadBackwardsEmptyTests : VirtualStreamReaderTests {
		[Fact]
		public void read_backwards_empty() {
			var correlation = Guid.NewGuid();

			var result = _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10));

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
		public void read_backwards() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10));

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
		public void read_backwards_beyond_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 5, maxCount: 10));

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
		public void read_backwards_far_beyond_latest_event() {
			// we specify maxCount, not a lower event number, so it is acceptable in this case to either
			// - find event 0 (like we do for regular stream forward maxAge reads if old events have been scavenged)
			//   and reach the end of the stream (nextEventNumber == -1)
			// - not find event 0 (like we do for regular maxCount reads, even if old events have been scavenged)
			//   and not reach the end of the stream (nextEventNumber >= 0 so that we can read it in subsequent pages)
			// current implementation finds the event.
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 1000, maxCount: 10));

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
		public void read_backwards_below_latest_event() {
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			_listener.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
			var correlation = Guid.NewGuid();

			var result = _sut.ReadBackwards(GenReadBackwards(correlation, fromEventNumber: 0, maxCount: 10));

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
	}
}
