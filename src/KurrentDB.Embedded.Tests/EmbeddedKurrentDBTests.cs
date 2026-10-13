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
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Embedded.Tests;

[Timeout(180_000)]
public class EmbeddedKurrentDBTests {
	[ClassDataSource<EmbeddedDatabaseFixture>(Shared = SharedType.PerClass)]
	public required EmbeddedDatabaseFixture Fixture { get; init; }

	[Test]
	public async Task appends_and_reads_back_over_the_socket(CancellationToken cancellationToken) {
		await using var client = Fixture.CreateClient();
		var streamName = $"embedded-{Guid.NewGuid():N}";

		await client.AppendToStreamAsync(
			streamName,
			StreamState.Any,
			[
				TestEvent("first", new { index = 1 }),
				TestEvent("second", new { index = 2 })
			],
			cancellationToken: cancellationToken);

		var read = await ReadStream(client, streamName, cancellationToken);

		read.Count.ShouldBe(2);
		read[0].Event.EventType.ShouldBe("first");
		read[1].Event.EventType.ShouldBe("second");
		Encoding.UTF8.GetString(read[1].Event.Data.Span).ShouldContain("\"index\":2");
	}

	[Test]
	public async Task serves_http_over_the_socket(CancellationToken cancellationToken) {
		using var client = new HttpClient(CreateSocketHandler(Fixture.UnixSocketPath));

		// the address supplies the scheme and the authority and nothing else: the handler dials the socket
		using var response = await client.GetAsync("http://localhost/health/live", cancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task reaches_the_grpc_endpoint_over_the_socket(CancellationToken cancellationToken) {
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
			await channel.CreateCallInvoker()
				.AsyncUnaryCall(probe, host: null, new CallOptions(cancellationToken: cancellationToken), []));

		exception.StatusCode.ShouldBe(StatusCode.Unimplemented);
	}

	[Test]
	public async Task listens_on_nothing_but_the_socket(CancellationToken cancellationToken) {
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
	public async Task restricts_the_socket_to_its_owner(CancellationToken cancellationToken) {
		if (OperatingSystem.IsWindows())
			return; // UNIX file permissions do not apply

		File.GetUnixFileMode(Fixture.UnixSocketPath)
			.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);

		await Task.CompletedTask;
	}

	internal static EventData TestEvent<T>(string eventType, T payload) =>
		new(Uuid.NewUuid(), eventType, JsonSerializer.SerializeToUtf8Bytes(payload));

	internal static async Task<List<ResolvedEvent>> ReadStream(KurrentDBClient client, string streamName,
		CancellationToken cancellationToken) {
		var events = new List<ResolvedEvent>();

		await foreach (var resolved in client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start,
					   cancellationToken: cancellationToken))
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
/// The socket is bound inside the data directory, with whatever the umask allows, and is only narrowed
/// once the server is accepting connections. A directory other users cannot traverse is what closes that
/// window, so the data directory has to end up private whether or not it was already there.
/// </summary>
public class EmbeddedKurrentDBDataDirectoryTests {
	[Test]
	public async Task creates_a_data_directory_its_owner_alone_can_reach() {
		if (OperatingSystem.IsWindows())
			return; // UNIX file permissions do not apply

		var dataDirectory = TestPaths.NewDataDirectory();

		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));

			File.GetUnixFileMode(db.DataDirectory)
				.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}

	[Test]
	public async Task refuses_a_data_directory_the_socket_would_not_fit_in() {
		// the limit is on the socket address rather than the path, so this is a perfectly good directory
		var dataDirectory = TestPaths.NewDataDirectory() + new string('x', 120);

		var exception = Should.Throw<ArgumentException>(() =>
			new EmbeddedKurrentDB(TestPaths.Options(dataDirectory)));

		exception.Message.ShouldContain("UNIX domain socket");

		// it gave up before opening the database, or even creating the directory
		Directory.Exists(dataDirectory).ShouldBeFalse();

		await Task.CompletedTask;
	}

	[Test]
	public async Task narrows_a_data_directory_that_was_already_there() {
		if (OperatingSystem.IsWindows())
			return; // UNIX file permissions do not apply

		var dataDirectory = TestPaths.NewDataDirectory();
		Directory.CreateDirectory(dataDirectory);

		// anyone could have traversed this to the socket, for as long as the socket allowed it
		File.SetUnixFileMode(dataDirectory,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
			UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
			UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));

			File.GetUnixFileMode(db.DataDirectory)
				.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}
}

/// <summary>
/// The exclusive database lock is a named mutex, and a mutex can only be released by the thread that took
/// it. If the embedded database released it from the wrong thread, a second node on the same directory in
/// the same process would not be able to take it.
/// </summary>
[Timeout(300_000)]
public class EmbeddedKurrentDBRestartTests {
	[Test]
	public async Task reopens_the_same_data_directory_and_keeps_its_events(CancellationToken cancellationToken) {
		var dataDirectory = TestPaths.NewDataDirectory();
		var streamName = $"restart-{Guid.NewGuid():N}";

		try {
			await using (var first = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory))) {
				await first.StartAsync(cancellationToken);

				await using var client = new KurrentDBClient(first.CreateClientSettings());
				await client.AppendToStreamAsync(
					streamName,
					StreamState.Any,
					[EmbeddedKurrentDBTests.TestEvent("written-before-the-restart", new { ok = true })],
					cancellationToken: cancellationToken);
			}

			await using (var second = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory))) {
				await second.StartAsync(cancellationToken);

				await using var client = new KurrentDBClient(second.CreateClientSettings());
				var read = await EmbeddedKurrentDBTests.ReadStream(client, streamName, cancellationToken);

				read.Count.ShouldBe(1);
				read[0].Event.EventType.ShouldBe("written-before-the-restart");
			}
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}
}

/// <summary>
/// A node honours one stop and one only: a second one logs a warning and returns without waiting. So a
/// caller who gives up waiting must not leave disposal free to close the database underneath a node that
/// is still shutting down.
/// </summary>
[Timeout(300_000)]
public class EmbeddedKurrentDBCancelledStopTests {
	[Test]
	public async Task disposal_waits_for_the_shutdown_a_cancelled_stop_began(CancellationToken cancellationToken) {
		var dataDirectory = TestPaths.NewDataDirectory();

		try {
			var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));
			await db.StartAsync(cancellationToken);

			// not through the hosted services: LogHostedServiceLifecycle decorates every one of them
			var node = db.Node;
			var lifetime = db.Services.GetRequiredService<IHostApplicationLifetime>();

			// give up waiting once the host is on its way down. CancelAfter rather than Cancel so that none
			// of this runs inline on the thread that is running the shutdown we are about to wait for
			using var giveUpWaiting = new CancellationTokenSource();
			using var registration = lifetime.ApplicationStopping.Register(() => giveUpWaiting.CancelAfter(TimeSpan.Zero));

			try {
				await db.StopAsync(giveUpWaiting.Token);
			} catch (OperationCanceledException) {
				// the wait was abandoned, which is the case under test. The shutdown itself carries on
			}

			await db.DisposeAsync();

			node.IsShutdown.ShouldBeTrue();
		} finally {
			TestPaths.Delete(dataDirectory);
		}
	}
}
