// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.AutoScavenge.Converters;
using EventStore.AutoScavenge.Domain;
using EventStore.POC.IO.Core.Serialization;
using HttpMethod = System.Net.Http.HttpMethod;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.AutoScavenge.Tests;

public class PluginTests : IClassFixture<DummyWebApplicationFactory>{
	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = {
			new EnumConverterWithDefault<AutoScavengeStatus>(),
			new EnumConverterWithDefault<AutoScavengeStatusResponse.Status>(),
			new CrontableScheduleJsonConverter(),
		},
	};

	private readonly HttpClient _client;

	public PluginTests(DummyWebApplicationFactory factory) {
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task ShouldDenyAccessToEnabledEndpoint() {
		var resp = await _client.GetAsync("/auto-scavenge/enabled");
		Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
	}

	[Fact]
	public async Task ShouldDenyAccessToStatusEndpoint() {
		var resp = await _client.GetAsync("/auto-scavenge/status");
		Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
	}

	[Fact]
	public async Task ShouldDenyAccessToConfigureEndpointAuthenticatedWrongRole() {
		var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/configure");
		msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("johndoe:1234"u8.ToArray()));
		var resp = await _client.SendAsync(msg);

		Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
	}

	[Fact]
	public async Task ShouldDenyAccessToPauseEndpointAuthenticatedWrongRole() {
		var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/pause");
		msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("johndoe:1234"u8.ToArray()));
		var resp = await _client.SendAsync(msg);

		Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
	}

	[Fact]
	public async Task ShouldDenyAccessToResumeEndpointAuthenticatedWrongRole() {
		var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/resume");
		msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("johndoe:1234"u8.ToArray()));
		var resp = await _client.SendAsync(msg);

		Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
	}

	[Fact]
	public async Task ShouldAccessEnabledEndpointAuthenticated() {
		var msg = new HttpRequestMessage(HttpMethod.Get, "/auto-scavenge/enabled");
		msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
		var resp = await _client.SendAsync(msg);

		resp.EnsureSuccessStatusCode();

		var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
		Assert.True(payload!["enabled"]!);
	}

	[Fact]
	public async Task ShouldAccessStatusEndpoint() {
		var attempt = 0;
		HttpResponseMessage resp;

		do {
			var msg = new HttpRequestMessage(HttpMethod.Get, "/auto-scavenge/status");
			msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
			resp = await _client.SendAsync(msg);
			attempt++;

			var code = (int)resp.StatusCode;
			if (code < 500)
				break;
			await Task.Delay(TimeSpan.FromSeconds(1));
		} while (attempt < 30);

		resp.EnsureSuccessStatusCode();
		var payload = await resp.Content.ReadFromJsonAsync<AutoScavengeStatusResponse>(JsonSerializerOptions);

		Assert.True(payload != null);

		if (payload.Schedule != null) {
			Assert.True(payload.State is AutoScavengeStatusResponse.Status.Waiting or AutoScavengeStatusResponse.Status.Paused);
			Assert.NotNull(payload.TimeUntilNextCycle);
		} else {
			Assert.Equal(AutoScavengeStatusResponse.Status.NotConfigured, payload.State);
			Assert.Null(payload.TimeUntilNextCycle);
		}
	}

	[Fact]
	public async Task ShouldAccessConfigureEndpoint() {
		var attempt = 0;
		HttpResponseMessage resp;

		do {
			var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/configure");
			msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
			msg.Content = JsonContent.Create(new JsonObject {
				["schedule"] = "* * * * 1",
			});
			resp = await _client.SendAsync(msg);
			attempt++;

			var code = (int)resp.StatusCode;
			if (code < 500)
				break;
			await Task.Delay(TimeSpan.FromSeconds(1));
		} while (attempt < 30);

		resp.EnsureSuccessStatusCode();
	}

	[Fact]
	public async Task ShouldAccessPauseEndpoint() {
		var attempt = 0;
		HttpResponseMessage resp;

		do {
			var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/pause");
			msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
			resp = await _client.SendAsync(msg);
			attempt++;

			var code = (int)resp.StatusCode;
			if (code < 500)
				break;
			await Task.Delay(TimeSpan.FromSeconds(1));
		} while (attempt < 30);

		resp.EnsureSuccessStatusCode();
	}

	[Fact]
	public async Task ShouldAccessResumeEndpoint() {
		var attempt = 0;
		HttpResponseMessage resp;

		do {
			var msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/resume");
			msg.Headers.Authorization = new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
			resp = await _client.SendAsync(msg);
			attempt++;

			var code = (int)resp.StatusCode;
			if (code < 500)
				break;
			await Task.Delay(TimeSpan.FromSeconds(1));
		} while (attempt < 30);

		resp.EnsureSuccessStatusCode();
	}
}
