// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.InMemory;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Storage.InMemory;

public class GossipListenerServiceTests {
	private readonly GossipListenerService _sut;
	private readonly ChannelReader<Message> _channelReader;
	private readonly Guid _nodeId = Guid.NewGuid();

	private readonly JsonSerializerOptions _options = new() {
		Converters = {
			new JsonStringEnumConverter(),
		},
	};

	public GossipListenerServiceTests() {
		var channel = Channel.CreateUnbounded<Message>();
		_channelReader = channel.Reader;
		_sut = new GossipListenerService(
			nodeId: _nodeId,
			publisher: new EnvelopePublisher(new ChannelEnvelope(channel)),
			new InMemoryLog());
	}

	[Fact]
	public async Task notify_state_change() {
		static int random() => Random.Shared.Next(65000);

		var member = MemberInfo.ForVNode(
				instanceId: Guid.NewGuid(),
				timeStamp: DateTime.Now,
				state: Data.VNodeState.DiscoverLeader,
				isAlive: true,
				internalTcpEndPoint: default,
				internalSecureTcpEndPoint: new DnsEndPoint("myhost", random()),
				externalTcpEndPoint: default,
				externalSecureTcpEndPoint: new DnsEndPoint("myhost", random()),
				httpEndPoint: new DnsEndPoint("myhost", random()),
				advertiseHostToClientAs: "advertiseHostToClientAs",
				advertiseHttpPortToClientAs: random(),
				advertiseTcpPortToClientAs: random(),
				lastCommitPosition: random(),
				writerCheckpoint: random(),
				chaserCheckpoint: random(),
				epochPosition: random(),
				epochNumber: random(),
				epochId: Guid.NewGuid(),
				nodePriority: random(),
				isReadOnlyReplica: true);

		// when
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(member)));

		// then
		var @event = Assert.IsType<StorageMessage.InMemoryEventCommitted>(await _channelReader.ReadAsync());

		Assert.Equal(SystemStreams.GossipStream, @event.Event.EventStreamId);
		Assert.Equal(GossipListenerService.EventType, @event.Event.EventType);
		Assert.Equal(0, @event.Event.EventNumber);

		var expectedBytes = JsonSerializer.SerializeToUtf8Bytes(
			new {
				NodeId = _nodeId,
				Members = new[] { new ClientClusterInfo.ClientMemberInfo(member) },
			},
			_options);

		var actualBytes = @event.Event.Data;

		Assert.Equal(
			Encoding.UTF8.GetString(expectedBytes),
			Encoding.UTF8.GetString(actualBytes.Span));

		Assert.Equal(expectedBytes, actualBytes);
	}
}
