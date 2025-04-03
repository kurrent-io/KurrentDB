// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EventStore.POC.IO.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.AutoScavenge.Tests;

public class CrashTests: IClassFixture<DummyWebApplicationFactory> {
	private readonly HttpClient _client;

	public CrashTests(DummyWebApplicationFactory factory) {
		_client = factory.CreateClient();
		((DummyClient)factory.Services.GetRequiredService<IClient>()).BeNaughty = true;
	}

	[Fact]
	public async Task ShouldReturnResponseEvenWhenCrashing() {
		var attempt = 0;
		HttpResponseMessage resp;
		HttpRequestMessage msg;

		// We make sure the server is ready to accept configure request first.
		do {
			msg = new HttpRequestMessage(HttpMethod.Get, "/auto-scavenge/status");
			msg.Headers.Authorization =
				new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
			resp = await _client.SendAsync(msg);
			attempt++;

			var code = (int)resp.StatusCode;
			if (code < 500)
				break;
			await Task.Delay(TimeSpan.FromSeconds(1));
		} while (attempt < 30);

		resp.EnsureSuccessStatusCode();

		msg = new HttpRequestMessage(HttpMethod.Post, "/auto-scavenge/configure");
		msg.Headers.Authorization =
			new AuthenticationHeaderValue("dummy", Convert.ToBase64String("ouro:changeit"u8.ToArray()));
		msg.Content = JsonContent.Create(new JsonObject {
			["schedule"] = "* * * * 1",
		});


		resp = await _client.SendAsync(msg);
		Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
	}
}
