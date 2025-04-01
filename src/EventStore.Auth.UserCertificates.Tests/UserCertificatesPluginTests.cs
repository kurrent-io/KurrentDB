// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventStore.Auth.UserCertificates.Tests;

public class UserCertificatesPluginTests {
	private readonly FakeLogger _logger = new();
	private readonly X509Certificate2 _rootCert;
	private readonly X509Certificate2 _nodeCert;
	private readonly X509Certificate2 _userCert;

	public UserCertificatesPluginTests() {
		_rootCert = TestUtils.CreateCertificate(ca: true, parent: null);
		_nodeCert = TestUtils.CreateCertificate(ca: false, parent: _rootCert, clientAuthEKU: true, serverAuthEKU: true);
		_userCert = TestUtils.CreateCertificate(ca: false, parent: _rootCert, clientAuthEKU: true);
		_userCert = new X509Certificate2(_userCert.Export(X509ContentType.Pfx));
	}

	[Fact]
	public void has_parameterless_constructor() {
		// needed for all plugins
		Activator.CreateInstance<UserCertificatesPlugin>();
	}

	[Fact]
	public async Task works() {
		await using var app = await CreateServer(
			new Dictionary<string, string?> {
				{ $"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", "true" }
			},
			ConfigureServicesCorrectly);

		var client = CreateClient(app, _userCert);
		var result = await client.GetAsync("/test");
		Assert.Equal(HttpStatusCode.OK, result.StatusCode);

		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("UserCertificatesPlugin: user X.509 certificate authentication is enabled", log.RenderMessage());
	}

	[Fact]
	public async Task disabled_by_default() {
		using var collector = PluginDiagnosticsDataCollector.Start("UserCertificates");
		await using var app = await CreateServer([], ConfigureServicesCorrectly);

		var client = CreateClient(app, _userCert);
		var result = await client.GetAsync("/test");
		Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);

		collector.CollectedEvents("UserCertificates").Should().ContainSingle().Which
			.Data["enabled"].Should().Be(false);
	}

	[Fact]
	public async Task can_be_disabled() {
		using var collector = PluginDiagnosticsDataCollector.Start("UserCertificates");
		await using var app = await CreateServer(
			new Dictionary<string, string?> {
				{ $"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", "false" }
			},
			ConfigureServicesCorrectly);

		var client = CreateClient(app, _userCert);
		var result = await client.GetAsync("/test");
		Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);

		collector.CollectedEvents("UserCertificates").Should().ContainSingle().Which
			.Data["enabled"].Should().Be(false);
	}

	[Fact]
	public async Task requires_user_certificate_scheme() {
		await using var app = await CreateServer(
			new Dictionary<string, string?> {
				{ $"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", "true" }
			},
			services => services
				.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
				.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>([new FakeAnonymousHttpAuthenticationProvider()])
				.AddSingleton<IAuthenticationProvider>(new FakeAuthenticationProvider("Basic"))
				.AddSingleton<Func<(X509Certificate2? Node, X509Certificate2Collection? Intermediates, X509Certificate2Collection? Roots)>>(
				() => (_nodeCert, null, new(_rootCert))));

		var client = CreateClient(app, _userCert);
		var result = await client.GetAsync("/test");
		Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);

		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("UserCertificatesPlugin is not compatible with the current authentication provider", log.RenderMessage());
	}

	[Fact]
	public async Task requires_anonymous_http_provider() {
		await using var app = await CreateServer(
			new Dictionary<string, string?> {
				{ $"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", "true" }
			},
			services => services
				.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
				.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>([])
				.AddSingleton<IAuthenticationProvider>(new FakeAuthenticationProvider("UserCertificate"))
				.AddSingleton<Func<(X509Certificate2? Node, X509Certificate2Collection? Intermediates, X509Certificate2Collection? Roots)>>(
				() => (_nodeCert, null, new(_rootCert))));

		var client = CreateClient(app, _userCert);
		var result = await client.GetAsync("/test");
		// since there is no anonymous provider we do not get authenticated at all, hence different code
		Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);

		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("UserCertificatesPlugin failed to load as the conditions required to load the plugin were not met", log.RenderMessage());
	}

	private async Task<WebApplication> CreateServer(
		Dictionary<string, string?> configuration,
		Action<IServiceCollection>? configureServices = null) {

		var builder = WebApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(configuration);
		builder.WebHost
			.UseUrls("https://127.0.0.1:0")
			.ConfigureKestrel(x => x
				.ConfigureHttpsDefaults(y => {
					y.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
					y.ClientCertificateValidation = (certificate, chain, errors) => true;
				}));
		builder.Services.AddAuthorizationBuilder()
			.SetDefaultPolicy(new AuthorizationPolicyBuilder()
				.RequireAuthenticatedUser()
				.Build());
		builder.Services.AddAuthentication(o => {
			o.AddScheme<FakeAuthenticationHandler>("fake", "fake");
		});

		configureServices?.Invoke(builder.Services);

		var sut = new UserCertificatesPlugin(_logger);
		((IPlugableComponent)sut).ConfigureServices(builder.Services, builder.Configuration);

		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, builder.Configuration);

		app.MapGet("/test", () => "Hello World!").RequireAuthorization();

		await app.StartAsync();
		return app;
	}

	void ConfigureServicesCorrectly(IServiceCollection services) => services
			.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
			.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>([new FakeAnonymousHttpAuthenticationProvider()])
			.AddSingleton<IAuthenticationProvider>(new FakeAuthenticationProvider("UserCertificate"))
			.AddSingleton<Func<(X509Certificate2? Node, X509Certificate2Collection? Intermediates, X509Certificate2Collection? Roots)>>(
				() => (_nodeCert, null, new(_rootCert)));

	private static HttpClient CreateClient(WebApplication app, X509Certificate2? userCert) {
		var handler = new HttpClientHandler {
			ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
		};

		if (userCert != null)
			handler.ClientCertificates.Add(userCert);

		var client = new HttpClient(handler, disposeHandler: true) {
			BaseAddress = new Uri(app.Urls.First())
		};

		return client;
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_collect_telemetry(bool enabled) {
		// given
		using var sut = new UserCertificatesPlugin();
		using var collector = PluginDiagnosticsDataCollector.Start(sut.DiagnosticsName);

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", $"{enabled}"},
			})
			.Build();

		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService(true, "USER_CERTIFICATES"));
		builder.Services.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>([]);

		// when
		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		collector.CollectedEvents(sut.DiagnosticsName).Should().ContainSingle().Which
			.Data["enabled"].Should().Be(enabled);
	}

	[Theory]
	[InlineData(true, true, "USER_CERTIFICATES", false)]
	[InlineData(true, false, "USER_CERTIFICATES", true)]
	[InlineData(false, true, "USER_CERTIFICATES", false)]
	[InlineData(false, false, "USER_CERTIFICATES", false)]
	[InlineData(true, true, "NONE", true)]
	[InlineData(true, false, "NONE", true)]
	[InlineData(false, true, "NONE", false)]
	[InlineData(false, false, "NONE", false)]
	public void respects_license(bool enabled, bool licensePresent, string entitlement, bool expectedException) {
		// given
		using var sut = new UserCertificatesPlugin();

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:UserCertificates:Enabled", $"{enabled}"},
			})
			.Build();

		var builder = WebApplication.CreateBuilder();

		if (enabled)
			builder.Services.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>([]);

		var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}
}
