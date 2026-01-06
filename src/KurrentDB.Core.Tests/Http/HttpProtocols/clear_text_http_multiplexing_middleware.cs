// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KurrentDB.Core.Services.Transport.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Http.HttpProtocols;


public class Startup : IStartup {
	public IServiceProvider ConfigureServices(IServiceCollection services) {
		return services.AddRouting().BuildServiceProvider();
	}

	public void Configure(IApplicationBuilder app) {
		app.UseRouter(router => {
			router.MapGet("/test", async context => {
				await context.Response.WriteAsync("hello");
			});
		});
	}
}

[TestFixture]
public class clear_text_http_multiplexing_middleware {
	private WebApplication _host;
	private string _endpoint;

	[SetUp]
	public async Task SetUp() {
		var startup = new Startup();
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseKestrel(server => {
			server.Listen(IPAddress.Loopback, 0, listenOptions => {
				listenOptions.Use(next => new ClearTextHttpMultiplexingMiddleware(next).OnConnectAsync);
			});
		});
		startup.ConfigureServices(builder.Services);
		_host = builder.Build();
		startup.Configure(_host);
		await _host.StartAsync();
		_endpoint = _host.Urls.First();
	}

	[TearDown]
	public Task Teardown() {
		_host?.StopAsync();
		return Task.CompletedTask;
	}

	[Test]
	public async Task http1_request() {
		using var client = new HttpClient();
		var request = new HttpRequestMessage(HttpMethod.Get, _endpoint + "/test");
		var result = await client.SendAsync(request);
		Assert.AreEqual(new Version(1, 1), result.Version);
		Assert.AreEqual("hello", await result.Content.ReadAsStringAsync());
	}

	[Test]
	public async Task http2_request() {
		using var client = new HttpClient();
		var request = new HttpRequestMessage(HttpMethod.Get, _endpoint + "/test") {
			Version = new Version(2, 0),
			VersionPolicy = HttpVersionPolicy.RequestVersionExact
		};

		var result = await client.SendAsync(request);
		Assert.AreEqual(new Version(2, 0), result.Version);
		Assert.AreEqual("hello", await result.Content.ReadAsStringAsync());
	}

}
