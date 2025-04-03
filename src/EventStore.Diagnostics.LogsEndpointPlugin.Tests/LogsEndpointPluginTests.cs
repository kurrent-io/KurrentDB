// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;

namespace EventStore.Diagnostics.LogsEndpointPlugin.Tests;

public class LogsEndpointPluginTests :
	IClassFixture<LicensedFixture>,
	IClassFixture<UnlicensedFixture> {

	private readonly LicensedFixture _fixture;
	private readonly UnlicensedFixture _unlicensedFixture;
	private const string Endpoint = "/admin/logs";

	public LogsEndpointPluginTests(LicensedFixture fixture, UnlicensedFixture unlicensedFixture) {
		_fixture = fixture;
		_unlicensedFixture = unlicensedFixture;
	}

	[Fact]
	public async Task does_not_throw_when_configuration_is_missing() {
		var ex = await Record.ExceptionAsync(async () => {

			var sut = new LogsEndpointPlugin();

			var builder = WebApplication.CreateBuilder();

			builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService());
			sut.ConfigureServices(builder.Services, builder.Configuration);

			await using var app = builder.Build();

			sut.ConfigureApplication(app, builder.Configuration);
		});

		Assert.Null(ex);
	}

	[Fact]
	public async Task requires_authentication() {
		var result = await _fixture.UnauthenticatedClient.GetAsync(Endpoint);

		Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
	}

	[Fact]
	public async Task is_available_at_endpoint() {
		var result = await _fixture.AuthenticatedClient.GetAsync(Endpoint);

		Assert.Equal(HttpStatusCode.OK, result.StatusCode);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task respects_license(bool licensePresent) {
		// given
		LogsEndpointPluginFixture f = licensePresent ? _fixture : _unlicensedFixture;

		// when
		var result = await f.AuthenticatedClient.GetAsync(Endpoint);

		// then
		if (licensePresent) {
			Assert.Equal(HttpStatusCode.OK, result.StatusCode);
		} else {
			Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
		}
	}

	[Fact]
	public void can_collect_telemetry() {
		// given
		var sut = new LogsEndpointPlugin();
		using var collector = PluginDiagnosticsDataCollector.Start(sut.DiagnosticsName);

		var config = new ConfigurationBuilder().Build();
		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService());

		// when
		((IPlugableComponent)sut).ConfigureServices(builder.Services, config);
		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		collector.CollectedEvents(sut.DiagnosticsName).Should().ContainSingle().Which
			.Data["enabled"].Should().Be(true);
	}
}

public class LicensedFixture : LogsEndpointPluginFixture {
	public LicensedFixture() : base(licensed: true) {
	}
}

public class UnlicensedFixture : LogsEndpointPluginFixture {
	public UnlicensedFixture() : base(licensed: false) {
	}
}

public class LogsEndpointPluginFixture : IAsyncLifetime {
	private WebApplication? _app;
	private HttpClient? _authenticatedClient;
	private HttpClient? _unauthenticatedClient;
	private readonly bool _licensed;

	public HttpClient AuthenticatedClient => _authenticatedClient!;
	public HttpClient UnauthenticatedClient => _unauthenticatedClient!;

	public LogsEndpointPluginFixture(bool licensed) {
		_licensed = licensed;
	}

	public async Task InitializeAsync() {
		var ip = "1.2.3.4";
		var port = "99";
		var fullPath = Path.Combine(Directory.GetCurrentDirectory(), $"{ip}-{port}-cluster-node");
		Directory.CreateDirectory(fullPath);

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");

		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>() {
			{$"{KurrentConfigurationConstants.Prefix}:Log", "./"},
			{$"{KurrentConfigurationConstants.Prefix}:NodeIp", ip},
			{$"{KurrentConfigurationConstants.Prefix}:NodePort", port}
		}!);

		var sut = new LogsEndpointPlugin();
		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService(_licensed, "LOGS_ENDPOINT"));
		sut.ConfigureServices(builder.Services, builder.Configuration);

		_app = builder.Build();

		_app.UseMiddleware<FakeBasicAuthMiddleware>();

		sut.ConfigureApplication(_app, builder.Configuration);

		await _app.StartAsync();

		_authenticatedClient = new HttpClient() {
			BaseAddress = new Uri(_app.Urls.First())
		};
		_authenticatedClient.BaseAddress = new Uri(_app!.Urls.First());
		_authenticatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
			Convert.ToBase64String(
				Encoding.ASCII.GetBytes($"{FakeBasicAuthMiddleware.Username}:{FakeBasicAuthMiddleware.Password}")));

		_unauthenticatedClient = new HttpClient() {
			BaseAddress = new Uri(_app.Urls.First())
		};
		_unauthenticatedClient.BaseAddress = new Uri(_app!.Urls.First());
	}

	public async Task DisposeAsync() {
		if (_app != null) {
			await _app.DisposeAsync();
		}
	}

	public class FakeBasicAuthMiddleware {
		public const string Username = "fake";
		public const string Password = "fake";

		private readonly RequestDelegate _next;

		public FakeBasicAuthMiddleware(RequestDelegate next) {
			_next = next;
		}

		public async Task Invoke(HttpContext context) {
			try {
				var authHeader = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization!);
				var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? "");
				var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
				var username = credentials[0];
				var password = credentials[1];

				if (username == Username && password == Password) {
					context.User = new ClaimsPrincipal(new ClaimsIdentity(new [] {
						new Claim(ClaimTypes.Name,"test-user"),
						new Claim(ClaimTypes.Role,"$ops"),
					}, "fake"));
				}
			}
			catch {
				// noop
			}

			await _next(context);
		}
	}
}
