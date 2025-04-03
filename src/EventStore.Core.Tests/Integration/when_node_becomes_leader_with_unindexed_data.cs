// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Core.Tests.Helpers;
using EventStore.Plugins.Subsystems;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Empty = EventStore.Client.Empty;
using RecordedEvent = EventStore.Client.Streams.ReadResp.Types.ReadEvent.Types.RecordedEvent;
using GrpcMetadata = EventStore.Core.Services.Transport.Grpc.Constants.Metadata;

namespace EventStore.Core.Tests.Integration;

[Explicit]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_node_becomes_leader_with_unindexed_data<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	private const string FakeHostAdvertiseAs = "192.168.123.123";
	private const string Username = "admin";
	private const string Password = "changeit";

	private const string AuthenticationScheme = "Basic";
	private readonly string AuthenticationValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Username}:{Password}"));
	private class CommitTimeoutException : Exception { }
	private class WrongExpectedVersionException : Exception { }

	private EndPoint[][] _nodeGossipSeeds;
	private HttpClient _httpClient;

	protected override async Task Given() {
		_nodeGossipSeeds = new[] {
			new EndPoint[] {_nodeEndpoints[1].HttpEndPoint, _nodeEndpoints[2].HttpEndPoint},
			new EndPoint[] {_nodeEndpoints[0].HttpEndPoint, _nodeEndpoints[2].HttpEndPoint},
			new EndPoint[] {_nodeEndpoints[0].HttpEndPoint, _nodeEndpoints[1].HttpEndPoint}
		};
		_httpClient = new HttpClient(new SocketsHttpHandler {
			SslOptions = {
				RemoteCertificateValidationCallback = delegate { return true; }
			}
		}, true);
		await base.Given();
	}

	private CallOptions GetCallOptions() {
		return new(
			credentials: CallCredentials.FromInterceptor((_, metadata) => {
				metadata.Add("authorization", $"{AuthenticationScheme} {AuthenticationValue}");
				return Task.CompletedTask;
			}),
			deadline: DateTime.UtcNow.AddSeconds(5));
	}

	private async Task AppendEvent(IPEndPoint endpoint, string stream, long expectedVersion) {
		using var channel = GrpcChannel.ForAddress(new Uri($"https://{endpoint}"),
			new GrpcChannelOptions { HttpClient = _httpClient });
		var streamClient = new Streams.StreamsClient(channel);
		using var call = streamClient.Append(GetCallOptions());

		var optionsAppendReq = new AppendReq {
			Options = new() {
				StreamIdentifier = new() {
					StreamName = ByteString.CopyFromUtf8(stream)
				},
			}
		};
		switch (expectedVersion) {
			case ExpectedVersion.Any:
				optionsAppendReq.Options.Any = new Empty();
				break;
			case ExpectedVersion.NoStream:
				optionsAppendReq.Options.NoStream = new Empty();
				break;
			default:
				optionsAppendReq.Options.Revision = (ulong) expectedVersion;
				break;
		}

		await call.RequestStream.WriteAsync(optionsAppendReq);
		await call.RequestStream.WriteAsync(new AppendReq {
			ProposedMessage = new() {
				Id = new() {
					String = Uuid.FromGuid(Guid.NewGuid()).ToString()
				},
				Data = ByteString.Empty,
				Metadata = {
					[GrpcMetadata.Type] = "type",
					[GrpcMetadata.ContentType] = GrpcMetadata.ContentTypes.ApplicationJson
				}
			}
		});

		await call.RequestStream.CompleteAsync();
		try {
			var appendResp = await call.ResponseAsync;
			switch (appendResp.ResultCase)
			{
				case AppendResp.ResultOneofCase.Success:
					return;
				case AppendResp.ResultOneofCase.WrongExpectedVersion:
					throw new WrongExpectedVersionException();
			}
		}
		catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.DeadlineExceeded) {
			throw new CommitTimeoutException();
		}
	}

	private async Task<IEnumerable<RecordedEvent>> ReadAllEvents(IPEndPoint endpoint) {
		using var channel = GrpcChannel.ForAddress(new Uri($"https://{endpoint}"),
			new GrpcChannelOptions { HttpClient = _httpClient });
		var streamClient = new Streams.StreamsClient(channel);

		using var call = streamClient.Read(new ReadReq {
			Options = new() {
				All = new() {
					Start = new Empty()
				},
				ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
				Count = ulong.MaxValue,
				NoFilter = new Empty(),
				UuidOption = new() { Structured = new() }
			}
		}, GetCallOptions());

		return await (from response in call.ResponseStream.ReadAllAsync() where response.Event != null select response.Event.Event).ToListAsync();
	}

	private MiniClusterNode<TLogFormat, TStreamId> CreateNode(int index, Endpoints endpoints, EndPoint[] gossipSeeds,
		int nodePriority, string intHostAdvertiseAs) => new(
		PathName, index, endpoints.InternalTcp,
		endpoints.ExternalTcp, endpoints.HttpEndPoint,
		subsystems: Array.Empty<ISubsystem>(), gossipSeeds: gossipSeeds, inMemDb: false,
		nodePriority: nodePriority, intHostAdvertiseAs: intHostAdvertiseAs);

	private async Task StartNode(int i, int priority, string intHostAdvertiseAs = null) {
		_nodes[i] = CreateNode(i, _nodeEndpoints[i], _nodeGossipSeeds[i], priority, intHostAdvertiseAs);
		await _nodes[i].Start();
	}

	private async Task<HttpStatusCode> GetLiveStatus(IPEndPoint httpEndPoint){
		var response = await _httpClient.GetAsync($"https://{httpEndPoint}/health/live");
		return response.StatusCode;
	}

	private async Task WaitForAllNodesToBeLive() {
		for (int i = 0; i < 3; i++) {
			await WaitForNodeToBeLive(i);
		}
	}

	private async Task WaitForNodeToBeLive(int idx) {
		while (await GetLiveStatus(_nodes[idx].HttpEndPoint) != HttpStatusCode.NoContent) {
			await Task.Delay(100);
		}
	}

	private async Task WaitForAllNodesToBeCaughtUp(int maxIdx = 3) {
		while (true) {
			var prevWriter = long.MinValue;
			var prevChaser = long.MinValue;
			var caughtUp = true;
			for (int i = 0; i < maxIdx; i++) {
				var writer = _nodes[i].Db.Config.WriterCheckpoint.ReadNonFlushed();
				var chaser = _nodes[i].Db.Config.ChaserCheckpoint.ReadNonFlushed();

				if (prevWriter == long.MinValue) prevWriter = writer;
				if (prevChaser == long.MinValue) prevChaser = chaser;
				if (chaser != writer || writer != prevWriter) {
					caughtUp = false;
				}
			}

			if (caughtUp) break;
			await Task.Delay(100);
		}
	}

	private async Task ShutdownNode(int i, bool keepDb) => await _nodes[i].Shutdown(keepDb: keepDb);

	private async Task ShutdownAllNodes(int maxIdx = 3, bool keepDb = false) {
		for (int i = 0; i < maxIdx; i++) {
			await ShutdownNode(i, keepDb);
		}
	}

	private async Task ResignLeader(int leaderIdx) {
		var httpEndPoint = _nodes[leaderIdx].HttpEndPoint;
		var request = new HttpRequestMessage(HttpMethod.Post, $"https://{httpEndPoint}/admin/node/resign") {
			Content = new StringContent(""),
			Headers = {
				Authorization = new AuthenticationHeaderValue(AuthenticationScheme, AuthenticationValue)
			}
		};

		var response = await _httpClient.SendAsync(request);
		if (response.StatusCode != HttpStatusCode.OK) {
			throw new Exception($"Unexpected status code: {response.StatusCode}");
		}

		var start = DateTime.UtcNow;
		while (_nodes[leaderIdx].NodeState != VNodeState.Unknown && DateTime.UtcNow - start < TimeSpan.FromSeconds(2)) {
			await Task.Delay(100);
		}
	}

	[SetUp]
	public async Task SetUp() {
		// reset the node states between tests
		for (int i = 0; i < 3; i++) {
			await _nodes[i].Shutdown(keepDb: false);
		}

		for (int i = 0; i < 3; i++) {
			_nodes[i] = CreateNode(i, _nodeEndpoints[i], _nodeGossipSeeds[i], 0, null);
			await _nodes[i].Start();
		}
	}

	[TestCase(true)]
	[TestCase(false)]
	[Explicit, Category("LongRunning"), Timeout(80000), NonParallelizable]
	public async Task new_events_should_have_correct_event_numbers(bool appendInitialEvent) {
		await WaitForAllNodesToBeLive();

		if (appendInitialEvent) {
			// append event 0@test
			await AppendEvent(_nodes[0].HttpEndPoint, "test", ExpectedVersion.NoStream);
		}

		await WaitForAllNodesToBeCaughtUp();
		await ShutdownAllNodes(keepDb: true);

		// make node 1 become the leader by setting its priority to 1
		// node 0 can't become a follower since it can't replicate over internal TCP due to the fake --int-host-advertise-as
		await StartNode(0, priority: 0, intHostAdvertiseAs: FakeHostAdvertiseAs);
		await StartNode(1, priority: 1, intHostAdvertiseAs: FakeHostAdvertiseAs);

		try {
			await WaitForNodeToBeLive(1).WithTimeout(TimeSpan.FromSeconds(10));
			Assert.AreEqual(VNodeState.Leader, _nodes[1].NodeState);
		} catch (TimeoutException) {
			// want to get stuck in preleader since replication isn't possible
			Assert.AreEqual(VNodeState.PreLeader, _nodes[1].NodeState);
			return;
		}

		// append event 1@test. Expect a commit timeout since there is no quorum.
		Assert.ThrowsAsync<CommitTimeoutException>(async () => {
			await AppendEvent(_nodes[1].HttpEndPoint, "test", appendInitialEvent ? 0 : ExpectedVersion.NoStream);
		});

		// resign the leader node so that it goes into the Unknown state and to trigger new elections
		await ResignLeader(1);

		// wait for the node to become Leader again
		while (_nodes[1].NodeState != VNodeState.Leader) {
			await Task.Delay(100);
		}

		// append event 1@test again. Expect a commit timeout since there is no quorum.
		Assert.ThrowsAsync<CommitTimeoutException>(async () => {
			await AppendEvent(_nodes[1].HttpEndPoint, "test", appendInitialEvent ? 0 : ExpectedVersion.NoStream);
		});

		// shut down both nodes
		await ShutdownAllNodes(maxIdx: 2, keepDb: true);

		// start both nodes again without the fake --int-host-advertise-as so that they can form a cluster
		await StartNode(0, priority: 0);
		await StartNode(1, priority: 1);
		try {
			await _nodes[0].Started.WithTimeout(TimeSpan.FromSeconds(10));
			await _nodes[1].Started.WithTimeout(TimeSpan.FromSeconds(10));
		} catch (TimeoutException) {
			// this is expected in logv3 because by creating the duplicate events above we also
			// created duplicate stream records which it will detect and complain about it
			throw new Exception($"Couldn't start one or more nodes: {_nodes[0].NodeState} {_nodes[1].NodeState}");
		}
		Assert.AreEqual(VNodeState.Follower, _nodes[0].NodeState);
		Assert.AreEqual(VNodeState.Leader, _nodes[1].NodeState);

		// wait for data replication
		await WaitForAllNodesToBeCaughtUp(maxIdx: 2);

		// read "test" events from $all
		var events =
			(await ReadAllEvents(_nodes[1].HttpEndPoint))
			.Where(x => x.StreamIdentifier!.StreamName.ToStringUtf8() == "test").ToArray();

		Assert.AreEqual(appendInitialEvent ? 3 : 2, events.Length);
		for (int i = 0; i < (appendInitialEvent ? 3 : 2); i++) {
			Assert.AreEqual(i, events[i].StreamRevision, $"i = {i}, revision = {events[i].StreamRevision}");
		}
	}
}
