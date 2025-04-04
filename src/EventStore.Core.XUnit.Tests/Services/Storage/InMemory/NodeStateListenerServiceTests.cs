// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.InMemory;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Storage.InMemory;

public class NodeStateListenerServiceTests {
	private readonly NodeStateListenerService _sut;
	private readonly ChannelReader<Message> _channelReader;

	public NodeStateListenerServiceTests() {
		var channel = Channel.CreateUnbounded<Message>();
		_channelReader = channel.Reader;
		_sut = new NodeStateListenerService(
			new EnvelopePublisher(new ChannelEnvelope(channel)),
			new InMemoryLog());
	}

	[Fact]
	public async Task notify_state_change() {
		_sut.Handle(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		var @event = Assert.IsType<StorageMessage.InMemoryEventCommitted>(await _channelReader.ReadAsync());

		Assert.Equal(SystemStreams.NodeStateStream, @event.Event.EventStreamId);
		Assert.Equal(NodeStateListenerService.EventType, @event.Event.EventType);
		Assert.Equal(0, @event.Event.EventNumber);
		Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(new {
			State = VNodeState.Leader.ToString(),
		}), @event.Event.Data.ToArray());
	}
}
