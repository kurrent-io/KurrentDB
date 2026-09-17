// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KurrentDB.Components.Tests;

// The middleware keeps a shutting-down server from handing out a new circuit, which would otherwise keep
// Kestrel draining until HostOptions.ShutdownTimeout expires. See BlazorShutdownMiddleware for the mechanism
// and https://github.com/dotnet/aspnetcore/issues/58947 for the upstream issue it works around.
public class BlazorShutdownMiddlewareTests {
	class FakeLifetime : IHostApplicationLifetime, IDisposable {
		readonly CancellationTokenSource _stopping = new();

		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => _stopping.Token;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public void StopApplication() => _stopping.Cancel();
		public void Dispose() => _stopping.Dispose();
	}

	// DefaultHttpContext's own lifetime feature ignores Abort(), so stand in for the one Kestrel provides.
	class RecordingRequestLifetime : IHttpRequestLifetimeFeature, IDisposable {
		readonly CancellationTokenSource _aborted = new();

		public bool Aborted { get; private set; }
		public CancellationToken RequestAborted { get => _aborted.Token; set { } }

		public void Abort() {
			Aborted = true;
			_aborted.Cancel();
		}

		public void Dispose() => _aborted.Dispose();
	}

	static DefaultHttpContext Request(string path) {
		var context = new DefaultHttpContext();
		context.Request.Path = path;
		return context;
	}

	[Fact]
	public async Task passes_other_requests_through() {
		using var lifetime = new FakeLifetime();
		lifetime.StopApplication();
		var called = false;
		var sut = new BlazorShutdownMiddleware(_ => {
			called = true;
			return Task.CompletedTask;
		}, lifetime);
		var context = Request("/ui/cluster");

		await sut.Invoke(context);

		Assert.True(called);
		Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
	}

	[Theory]
	// refusing the negotiation stops a new circuit being created while the server drains; refusing the
	// transport covers a client that negotiated just before the shutdown began
	[InlineData("/_blazor/negotiate")]
	[InlineData("/_blazor")]
	public async Task rejects_circuit_requests_while_stopping(string path) {
		using var lifetime = new FakeLifetime();
		lifetime.StopApplication();
		var called = false;
		var sut = new BlazorShutdownMiddleware(_ => {
			called = true;
			return Task.CompletedTask;
		}, lifetime);
		var context = Request(path);

		await sut.Invoke(context);

		Assert.False(called);
		Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
	}

	[Fact]
	public async Task aborts_a_circuit_that_started_before_the_shutdown() {
		using var lifetime = new FakeLifetime();
		using var requestLifetime = new RecordingRequestLifetime();
		var context = Request("/_blazor");
		context.Features.Set<IHttpRequestLifetimeFeature>(requestLifetime);
		var inFlight = new TaskCompletionSource();
		var sut = new BlazorShutdownMiddleware(circuit => {
			// a circuit request runs for as long as the browser tab lives, so it is still in flight when the
			// server begins stopping. Nothing else ends it, so without the abort the drain would wait for it
			// until the shutdown timeout expires.
			circuit.RequestAborted.Register(inFlight.SetResult);
			return inFlight.Task;
		}, lifetime);

		var pending = sut.Invoke(context);
		Assert.False(pending.IsCompleted);

		lifetime.StopApplication();

		await pending.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(requestLifetime.Aborted);
	}
}
