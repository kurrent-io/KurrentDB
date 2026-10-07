// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using KurrentDB.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace KurrentDB.Embedded.Tests;

[Timeout(180_000)]
public class EmbeddedKurrentDBTests {
	[ClassDataSource<EmbeddedDatabaseFixture>(Shared = SharedType.PerClass)]
	public required EmbeddedDatabaseFixture Fixture { get; init; }

	[Test]
	public async Task appends_and_reads_back_over_the_socket() {
		await using var client = Fixture.CreateClient();
		var streamName = $"embedded-{Guid.NewGuid():N}";

		await client.AppendToStreamAsync(
			streamName,
			StreamState.Any,
			[
				TestEvent("first", new { index = 1 }),
				TestEvent("second", new { index = 2 })
			]);

		var read = await ReadStream(client, streamName);

		read.Count.ShouldBe(2);
		read[0].Event.EventType.ShouldBe("first");
		read[1].Event.EventType.ShouldBe("second");
		Encoding.UTF8.GetString(read[1].Event.Data.Span).ShouldContain("\"index\":2");
	}

	[Test]
	public async Task serves_http_over_the_socket() {
		using var client = new HttpClient(CreateSocketHandler(Fixture.UnixSocketPath));

		// the address supplies the scheme and the authority and nothing else: the handler dials the socket
		using var response = await client.GetAsync("http://localhost/health/live");

		response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task reaches_the_grpc_endpoint_over_the_socket() {
		using var channel = Fixture.Database.CreateChannel();

		// a method the server does not have: reaching it at all is the point, and an answer of
		// "unimplemented" can only have come from the server
		var probe = new Method<byte[], byte[]>(
			MethodType.Unary,
			"kurrentdb.embedded.tests.Probe",
			"Probe",
			Marshallers.Create<byte[]>(request => request, response => response),
			Marshallers.Create<byte[]>(request => request, response => response));

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await channel.CreateCallInvoker().AsyncUnaryCall(probe, host: null, new CallOptions(), []));

		exception.StatusCode.ShouldBe(StatusCode.Unimplemented);
	}

	[Test]
	public async Task listens_on_nothing_but_the_socket() {
		var addresses = Fixture.Database.Services
			.GetRequiredService<IServer>()
			.Features
			.Get<IServerAddressesFeature>()!
			.Addresses;

		addresses.Count.ShouldBe(1);
		addresses.Single().ShouldContain(Fixture.UnixSocketPath);

		await Task.CompletedTask;
	}

	[Test]
	public async Task restricts_the_socket_to_its_owner() {
		if (OperatingSystem.IsWindows())
			return; // UNIX file permissions do not apply

		File.GetUnixFileMode(Fixture.UnixSocketPath)
			.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);

		await Task.CompletedTask;
	}

	internal static EventData TestEvent<T>(string eventType, T payload) =>
		new(Uuid.NewUuid(), eventType, JsonSerializer.SerializeToUtf8Bytes(payload));

	internal static async Task<List<ResolvedEvent>> ReadStream(KurrentDBClient client, string streamName) {
		var events = new List<ResolvedEvent>();

		await foreach (var resolved in client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start))
			events.Add(resolved);

		return events;
	}

	internal static SocketsHttpHandler CreateSocketHandler(string socketPath) =>
		new() {
			ConnectCallback = async (_, cancellationToken) => {
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try {
					await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
					return new NetworkStream(socket, ownsSocket: true);
				} catch {
					socket.Dispose();
					throw;
				}
			}
		};
}

/// <summary>
/// The exclusive database lock is a named mutex, and a mutex can only be released by the thread that took
/// it. If the embedded database released it from the wrong thread, a second node on the same directory in
/// the same process would not be able to take it.
/// </summary>
[Timeout(300_000)]
public class EmbeddedKurrentDBRestartTests {
	[Test]
	public async Task reopens_the_same_data_directory_and_keeps_its_events() {
		var dataDirectory = TestPaths.NewDataDirectory();
		var streamName = $"restart-{Guid.NewGuid():N}";

		try {
			await using (var first = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory))) {
				await first.StartAsync();

				await using var client = new KurrentDBClient(first.CreateClientSettings());
				await client.AppendToStreamAsync(
					streamName,
					StreamState.Any,
					[EmbeddedKurrentDBTests.TestEvent("written-before-the-restart", new { ok = true })]);
			}

			await using (var second = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory))) {
				await second.StartAsync();

				await using var client = new KurrentDBClient(second.CreateClientSettings());
				var read = await EmbeddedKurrentDBTests.ReadStream(client, streamName);

				read.Count.ShouldBe(1);
				read[0].Event.EventType.ShouldBe("written-before-the-restart");
			}
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}
}
