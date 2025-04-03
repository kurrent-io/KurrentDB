// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Core.Cluster;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Telemetry;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Plugins;
using Microsoft.Extensions.Configuration;
using Xunit;
using static EventStore.Plugins.Diagnostics.PluginDiagnosticsDataCollectionMode;

namespace EventStore.Core.XUnit.Tests.Telemetry;

public sealed class TelemetryServiceTests : IAsyncLifetime {
	private readonly TelemetryService _sut;
	private readonly InMemoryTelemetrySink _sink;
	private readonly ChannelReader<Message> _channelReader;
	private readonly TFChunkDb _db;
	private readonly DirectoryFixture<TelemetryServiceTests> _fixture = new();

	readonly FakePlugableComponent _plugin;

	public TelemetryServiceTests() {
		var config = TFChunkHelper.CreateSizedDbConfig(_fixture.Directory, 0, chunkSize: 4096);
		_db = new TFChunkDb(config);
		using (var task = _db.Open(false).AsTask()) {
			task.Wait();
		}

		var channel = Channel.CreateUnbounded<Message>();
		_channelReader = channel.Reader;
		_sink = new InMemoryTelemetrySink();

		_plugin = new();

		_sut = new TelemetryService(
			_db.Manager,
			new ClusterVNodeOptions().WithPlugableComponent(_plugin),
			new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>() {
				{ $"{KurrentConfigurationKeys.Prefix}:Telemetry:CloudIdentifier", "abc"},
			}).Build(),
			new EnvelopePublisher(new ChannelEnvelope(channel)),
			_sink,
			new InMemoryCheckpoint(0),
			Guid.NewGuid());
	}

	public Task InitializeAsync() => _fixture.InitializeAsync();

	public async Task DisposeAsync() {
		_plugin.Dispose();
		await _sut.DisposeAsync();
		await _db.DisposeAsync();
		await _fixture.DisposeAsync();
	}

	private static MemberInfo CreateMemberInfo(Guid instanceId, VNodeState state, bool isReadOnly) {
		static int random() => Random.Shared.Next(65000);

		var memberInfo = MemberInfo.ForVNode(
				instanceId: instanceId,
				timeStamp: DateTime.Now,
				state: state,
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
				isReadOnlyReplica: isReadOnly);

		return memberInfo;
	}

	[Fact]
	public async Task can_collect_and_flush_telemetry() {
		_plugin.PublishSomeTelemetry();

		// receive schedule of collect trigger it
		var schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);
		schedule.Reply();

		// receive the gossip request the telemetry service sends.
		var gossipRequest = Assert.IsType<GossipMessage.ReadGossip>(await _channelReader.ReadAsync());
		gossipRequest.Envelope.ReplyWith(new GossipMessage.SendGossip(
			new ClusterInfo(
				CreateMemberInfo(Guid.Empty, VNodeState.DiscoverLeader, true),
				CreateMemberInfo(Guid.Empty, VNodeState.DiscoverLeader, true)),
			new DnsEndPoint("localhost", 123)));

		// receive usage request and send response
		var request = Assert.IsType<TelemetryMessage.Request>(await _channelReader.ReadAsync());
		request.Envelope.ReplyWith(new TelemetryMessage.Response(
			"foo",
			new JsonObject {
				["bar"] = 42,
			}));

		// receive schedule of flush and trigger it
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Flush>(schedule.ReplyMessage);
		schedule.Reply();

		// receive schedule of collect indicating flush is complete
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);

		// check sink has received the data
		Assert.NotNull(_sink.Data["foo"]);
		Assert.Equal(new JsonObject { ["bar"] = 42 }.ToString(), _sink.Data["foo"].ToString());

		Assert.NotNull(_sink.Data["fakeComponent"]);
		Assert.Equal("""
			{
			  "baz": "qux"
			}
			""",
			_sink.Data["fakeComponent"].ToString());

		Assert.Equal(_sink.Data["environment"]!["os"]!.ToString(), RuntimeInformation.OSDescription);

		Assert.NotNull(_sink.Data["telemetry"]);
		Assert.Equal("""
			{
			  "cloudIdentifier": "abc"
			}
			""",
			_sink.Data["telemetry"].ToString());
	}

	[Fact]

	public async Task check_for_leaderid_and_epochid() {
		_plugin.PublishSomeTelemetry();
		// receive schedule of collect trigger it
		var schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		var mem1 = CreateMemberInfo(Guid.NewGuid(), VNodeState.Leader, false);
		var mem2 = CreateMemberInfo(Guid.NewGuid(), VNodeState.Follower, false);
		var _electionsDoneMessage = new ElectionMessage.ElectionsDone(1, mem1.EpochNumber, mem1);
		var _leaderFoundMessage = new LeaderDiscoveryMessage.LeaderFound(mem1);
		var _replicaStateMessage = new SystemMessage.BecomeReadOnlyReplica(mem1.InstanceId, mem1);
		_sut.Handle(_electionsDoneMessage);
		_sut.Handle(_leaderFoundMessage);
		_sut.Handle(_replicaStateMessage);
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);
		schedule.Reply();

		// receive the gossip request the telemetry service sends.
		var gossipRequest = Assert.IsType<GossipMessage.ReadGossip>(await _channelReader.ReadAsync());
		gossipRequest.Envelope.ReplyWith(new GossipMessage.SendGossip(
			new ClusterInfo(
				mem1 , mem2),
			new DnsEndPoint("localhost", 123)));

		// receive usage request and send response
		var request = Assert.IsType<TelemetryMessage.Request>(await _channelReader.ReadAsync());
		request.Envelope.ReplyWith(new TelemetryMessage.Response(
			"foo",
			new JsonObject {
				["bar"] = 42,
			}));

		// receive schedule of flush and trigger it
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Flush>(schedule.ReplyMessage);
		schedule.Reply();

		// receive schedule of collect indicating flush is complete
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);

		// check sink has received the data
		Assert.NotNull(_sink.Data);
		Assert.Equal(Guid.Parse(_sink.Data["cluster"]["leaderId"].ToString()), _electionsDoneMessage.Leader.InstanceId);
		Assert.Equal(Int32.Parse(_sink.Data["database"]["epochNumber"].ToString()), _electionsDoneMessage.ProposalNumber);

		Assert.Equal(Guid.Parse(_sink.Data["cluster"]["leaderId"].ToString()), _leaderFoundMessage.Leader.InstanceId);
		Assert.Equal(Int32.Parse(_sink.Data["database"]["epochNumber"].ToString()), _leaderFoundMessage.Leader.EpochNumber);

		Assert.Equal(Guid.Parse(_sink.Data["cluster"]["leaderId"].ToString()), _replicaStateMessage.Leader.InstanceId);
		Assert.Equal(Int32.Parse(_sink.Data["database"]["epochNumber"].ToString()), _replicaStateMessage.Leader.EpochNumber);

		Assert.NotNull(_sink.Data["fakeComponent"]);
	}

	class FakePlugableComponent(string name = "FakeComponent") : Plugin(name) {
		public void PublishSomeTelemetry() {
			PublishDiagnosticsData(new() {
				["enabled"] = Enabled
			}, Snapshot);

			PublishDiagnosticsData(new() {
				["Baz"] = "qux"
			}, Snapshot);
		}
	}
}
