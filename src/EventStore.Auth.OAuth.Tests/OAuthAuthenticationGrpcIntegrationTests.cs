// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.Client;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Auth.OAuth.Tests;

public class OAuthAuthenticationGrpcIntegrationTests {
	private readonly ITestOutputHelper _output;

	public OAuthAuthenticationGrpcIntegrationTests(ITestOutputHelper output) {
		_output = output;
	}

	[FactRequiringDocker]
	public async Task admin() {
		using var fixture = await Fixture.Create(_output);
		var token = await fixture.IdentityServer.GetAccessToken("admin", "password");

		using var client = GetEventStoreClient(token);

		await client.AppendToStreamAsync("a-stream", StreamState.Any, new[] {
			new EventData(Uuid.NewUuid(), "-", ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty),
		});
		await client.ReadAllAsync(Direction.Forwards, Position.Start).ToArrayAsync();
	}

	[FactRequiringDocker]
	public async Task user() {
		using var fixture = await Fixture.Create(_output);
		var token = await fixture.IdentityServer.GetAccessToken("user", "password");

		using var client = GetEventStoreClient(token);

		await Assert.ThrowsAsync<AccessDeniedException>(() =>
			client.ReadAllAsync(Direction.Forwards, Position.Start).ToArrayAsync()
				.AsTask());
	}

	private EventStoreClient GetEventStoreClient(string token) {
		var settings = EventStoreClientSettings.Create("esdb://localhost:2113/?tlsVerifyCert=false");
		settings.DefaultCredentials = new UserCredentials(token);
		settings.LoggerFactory = new XUnitLoggerFactory(_output);
		return new(settings);
	}
}
