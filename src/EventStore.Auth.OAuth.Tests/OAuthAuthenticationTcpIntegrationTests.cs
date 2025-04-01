// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.SystemData;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Auth.OAuth.Tests;

public class OAuthAuthenticationTcpIntegrationTests {
	private static readonly string[] EnableTcp = {
		"KurrentDB__TcpPlugin__EnableExternalTcp=true"
	};

	private readonly ITestOutputHelper _output;

	public OAuthAuthenticationTcpIntegrationTests(ITestOutputHelper output) {
		_output = output;
	}

	[FactRequiringDocker]
	public async Task admin() {
		using var fixture = await Fixture.Create(_output, EnableTcp);
		var token = await fixture.IdentityServer.GetAccessToken("admin", "password");

		using var client = await GetEventStoreConnection(token);

		await client.AppendToStreamAsync("a-stream", ExpectedVersion.Any,
			new EventData(Guid.NewGuid(), "-", false, Array.Empty<byte>(), Array.Empty<byte>()));

		await client.ReadAllEventsForwardAsync(Position.Start, 100, false);
	}

	[FactRequiringDocker]
	public async Task user() {
		using var fixture = await Fixture.Create(_output, EnableTcp);
		var token = await fixture.IdentityServer.GetAccessToken("user", "password");

		using var client = await GetEventStoreConnection(token);

		await Assert.ThrowsAsync<AccessDeniedException>(() =>
			client.ReadAllEventsForwardAsync(Position.Start, 100, false));
	}

	private async Task<IEventStoreConnection> GetEventStoreConnection(string token) {
		var client = EventStoreConnection.Create(
			ConnectionSettings.Create()
				.DisableServerCertificateValidation()
				.SetDefaultUserCredentials(new UserCredentials(token))
				.UseCustomLogger(new XUnitLogger(_output)),
			new IPEndPoint(IPAddress.Loopback, 1113));
		await client.ConnectAsync();
		return client;
	}

	private class XUnitLogger : ILogger {
		private readonly ITestOutputHelper _output;

		public XUnitLogger(ITestOutputHelper output) {
			_output = output;
		}

		public void Error(string format, params object[] args) => Log(nameof(Error), format, args);

		public void Error(Exception ex, string format, params object[] args) =>
			Log(nameof(Error), ex, format, args);

		public void Info(string format, params object[] args) => Log(nameof(Info), format, args);
		public void Info(Exception ex, string format, params object[] args) => Log(nameof(Info), ex, format, args);
		public void Debug(string format, params object[] args) => Log(nameof(Debug), format, args);

		public void Debug(Exception ex, string format, params object[] args) =>
			Log(nameof(Debug), ex, format, args);

		private void Log(string level, string format, params object[] args) => Log(level, null, format, args);

		private void Log(string level, Exception ex, string format, params object[] args)
			=> _output.WriteLine($"[{level}] {string.Format(format, args)}{Environment.NewLine}{ex}");
	}
}
