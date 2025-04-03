// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventStore.Licensing.Tests;

public class LicensingPluginTests : IAsyncLifetime {
	private TestServer? _server;

	public Task InitializeAsync() => Task.CompletedTask;

	private static LicensingPlugin CreateUnLicensedSutAsync() {
		return new LicensingPlugin(
			ex => { },
			new AdHocLicenseProvider(new Exception("license is expired, say")));
	}

	private static async Task<LicensingPlugin> CreateLicensedSutAsync(Dictionary<string, object> claims) {
		var license = await License.CreateAsync(claims);
		return new LicensingPlugin(
			ex => { },
			new AdHocLicenseProvider(license));
	}

	private static async Task<TestServer> InitializeServerAsync(LicensingPlugin sut) {
		var server = new TestServer(sut);
		await server.InitializeAsync();
		return server;
	}

	public async Task DisposeAsync() {
		if (_server != null) {
			await _server.DisposeAsync();
		}
	}

	[Fact]
	public async Task given_licensed_then_endpoint_exposes_the_correct_claims() {
		// given
		var claims = new Dictionary<string, object> {
			{"another_claim", "another_value" } // excluded from endpoint
		};

		new LicenseSummary(
			licenseId: "license123",
			company: "the_company",
			isTrial: true,
			expiry: new DateTimeOffset(1970, 1, 1, 0, 0, 55, default),
			isValid: true,
			notes: "somenotes")
			.ExportClaims(claims);

		var sut = await CreateLicensedSutAsync(claims);
		_server = await InitializeServerAsync(sut);

		// when
		var result = await _server.AuthenticatedClient.GetAsync("/license");

		// then
		Assert.Equal(HttpStatusCode.OK, result.StatusCode);

		var responseString = await result.Content.ReadAsStringAsync();
		Assert.DoesNotContain("another", responseString);
		Assert.Equal("""
			{
			  "isExpired": "true",
			  "licenseId": "license123",
			  "company": "the_company",
			  "isTrial": "true",
			  "daysRemaining": "0.00",
			  "isValid": "true",
			  "notes": "somenotes",
			  "isFloating": "true",
			  "startDate": "0"
			}
			""".Replace(" ", "").Replace(Environment.NewLine, ""),
			responseString);
	}

	[Fact]
	public async Task given_not_licensed_then_endpoint_returns_404() {
		// given
		var sut = CreateUnLicensedSutAsync();
		_server = await InitializeServerAsync(sut);

		// when
		var result = await _server.AuthenticatedClient.GetAsync("/license");

		// then
		Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
		var responseString = await result.Content.ReadAsStringAsync();
		Assert.Equal("\"license is expired, say\"", responseString);
	}

	[Fact]
	public async Task requires_authentication() {
		// given
		var sut = await CreateLicensedSutAsync(new Dictionary<string, object> {
			{"isTrial", "true" }
		});
		_server = await InitializeServerAsync(sut);

		// when
		var result = await _server.UnauthenticatedClient.GetAsync("/license");

		// then
		Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
		var responseString = await result.Content.ReadAsStringAsync();
		Assert.Equal("", responseString);
	}
}

public class TestServer(LicensingPlugin sut) : IAsyncLifetime {
	private WebApplication? _app;
	private HttpClient? _authenticatedClient;
	private HttpClient? _unauthenticatedClient;

	public HttpClient AuthenticatedClient => _authenticatedClient!;
	public HttpClient UnauthenticatedClient => _unauthenticatedClient!;

	public async Task InitializeAsync() {
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Services.AddAuthorization();
		builder.Services.AddAuthentication(o => {
			o.AddScheme<FakeAuthenticationHandler>("fake", "fake");
		});

		((IPlugableComponent)sut).ConfigureServices(builder.Services, builder.Configuration);
		_app = builder.Build();
		_app.UseRouting();
		_app.UseAuthorization();

		((IPlugableComponent)sut).ConfigureApplication(_app, builder.Configuration);

		await _app.StartAsync();

		// create the clients
		_authenticatedClient = new HttpClient() {
			BaseAddress = new Uri(_app.Urls.First())
		};
		_authenticatedClient.BaseAddress = new Uri(_app!.Urls.First());
		_authenticatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
			"Basic",
			Convert.ToBase64String(Encoding.ASCII.GetBytes(
				$"{FakeAuthenticationHandler.Username}:{FakeAuthenticationHandler.Password}")));

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
}
