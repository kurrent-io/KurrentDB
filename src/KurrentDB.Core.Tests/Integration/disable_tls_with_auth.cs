// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Internal;
using EventStore.ClientAPI.SystemData;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;
using GrpcConstants = KurrentDB.Core.Services.Transport.Grpc.Constants;
using StatusCode = Grpc.Core.StatusCode;

namespace KurrentDB.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_running_with_disable_tls<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture {

	private MiniNode<TLogFormat, TStreamId> _node;

	private static readonly string BasicAuthHeader =
		$"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:changeit"))}";

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(
			pathname: PathName,
			disableTls: true);
		await _node.Start();
		await _node.AdminUserCreated;
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task unauthenticated_request_is_rejected() {
		// The node's built-in HttpClient has no credentials — use it directly
		// to verify that unauthenticated access to a protected resource is rejected
		var response = await _node.HttpClient.GetAsync("/streams/$all");
		Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Test]
	public async Task authenticated_request_succeeds() {
		using var request = new HttpRequestMessage(HttpMethod.Get, "/streams/$all");
		request.Headers.Authorization = AuthenticationHeaderValue.Parse(BasicAuthHeader);

		var response = await _node.HttpClient.SendAsync(request);
		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[Test]
	public async Task grpc_authenticated_append_succeeds() {
		// MiniNode runs Kestrel via UseTestServer() — there is no real TCP listener
		// on HttpEndPoint, so route the channel through the in-process TestServer
		// handler (the same handler the node's HttpClient already uses).
		using var channel = GrpcChannel.ForAddress(
			new Uri($"http://{_node.HttpEndPoint}"),
			new GrpcChannelOptions { HttpHandler = _node.HttpMessageHandler });

		var streamClient = new Streams.StreamsClient(channel);
		var stream = $"grpc-auth-{Guid.NewGuid():N}";
		var headers = new Metadata { { "authorization", BasicAuthHeader } };

		using var call = streamClient.Append(new CallOptions(
			headers: headers,
			deadline: DateTime.UtcNow.AddSeconds(10)));

		await call.RequestStream.WriteAsync(new AppendReq {
			Options = new AppendReq.Types.Options {
				NoStream = new EventStore.Client.Empty(),
				StreamIdentifier = new StreamIdentifier {
					StreamName = ByteString.CopyFromUtf8(stream),
				},
			},
		});
		await call.RequestStream.WriteAsync(new AppendReq {
			ProposedMessage = new AppendReq.Types.ProposedMessage {
				Id = new UUID { String = Uuid.FromGuid(Guid.NewGuid()).ToString() },
				CustomMetadata = ByteString.Empty,
				Data = ByteString.Empty,
				Metadata = {
					{ GrpcConstants.Metadata.Type, "-" },
					{ GrpcConstants.Metadata.ContentType, GrpcConstants.Metadata.ContentTypes.ApplicationOctetStream },
				},
			},
		});
		await call.RequestStream.CompleteAsync();
		await call.ResponseAsync;

		Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
	}

	[Test]
	public async Task tcp_authenticated_append_succeeds() {
		using var connection = EventStoreConnection.Create(
			ConnectionSettings.Create().DisableTls(),
			_node.TcpEndPoint.ToESTcpUri());
		await connection.ConnectAsync();

		var stream = $"tcp-auth-{Guid.NewGuid():N}";
		var result = await connection.AppendToStreamAsync(
			stream, ExpectedVersion.NoStream,
			new UserCredentials("admin", "changeit"),
			new EventData(Guid.NewGuid(), "-", false, Array.Empty<byte>(), Array.Empty<byte>()));

		Assert.GreaterOrEqual(result.NextExpectedVersion, 0);
	}

	[Test]
	public void node_reports_tls_disabled() {
		Assert.IsTrue(_node.Node.DisableHttps);
		Assert.AreEqual("http", _node.HttpClient.BaseAddress!.Scheme);
	}
}
