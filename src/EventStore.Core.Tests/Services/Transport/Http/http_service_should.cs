// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using System.Threading.Tasks;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture, Category("LongRunning")]
public class http_service_should : SpecificationWithDirectory {
	[Test]
	[Category("Network")]
	public async Task start_after_system_message_system_init_published() {
		await using var node = new MiniNode<LogFormat.V2,string>(PathName);
		await node.StartTestServer();

		Assert.IsFalse(node.Node.HttpService.IsListening);
		node.Node.MainQueue.Publish(new SystemMessage.SystemInit());
		AssertEx.IsOrBecomesTrue(() => node.Node.HttpService.IsListening);
	}

	[Test]
	[Category("Network")]
	public async Task ignore_shutdown_message_that_does_not_say_shut_down() {
		await using var node = new MiniNode<LogFormat.V2,string>(PathName);
		await node.StartTestServer();
		node.Node.MainQueue.Publish(new SystemMessage.SystemInit());

		AssertEx.IsOrBecomesTrue(() => node.Node.HttpService.IsListening);

		node.Node.MainQueue.Publish(
			new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), exitProcess: false, shutdownHttp: false));

		Assert.IsTrue(node.Node.HttpService.IsListening);
	}

	[Test]
	[Category("Network")]
	public async Task react_to_shutdown_message_that_cause_process_exit() {
		await using var node = new MiniNode<LogFormat.V2,string>(PathName);
		await node.StartTestServer();
		node.Node.MainQueue.Publish(new SystemMessage.SystemInit());

		AssertEx.IsOrBecomesTrue(() => node.Node.HttpService.IsListening);

		node.Node.MainQueue.Publish(
			new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), exitProcess: true, shutdownHttp: true));

		AssertEx.IsOrBecomesTrue(() => !node.Node.HttpService.IsListening);
	}

	[Test]
	[Category("Network")]
	public async Task handle_invalid_characters_in_url() {
		await using var node = new MiniNode<LogFormat.V2,string>(PathName);
		await node.StartTestServer();
		node.Node.MainQueue.Publish(new SystemMessage.SystemInit());

		var result = await node.HttpClient.GetAsync("/ping^\"");

		Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
		Assert.IsEmpty(await result.Content.ReadAsStringAsync());
	}
}

[TestFixture, Category("LongRunning")]
public class when_http_request_times_out : SpecificationWithDirectory {
	[Test]
	[Category("Network")]
	public async Task should_throw_an_exception() {
		var timeoutSec = 2;
		var sleepFor = timeoutSec + 1;

		await using var node = new MiniNode<LogFormat.V2, string>(PathName, httpClientTimeoutSec: timeoutSec);
		await node.Start();

		Assert.ThrowsAsync<TaskCanceledException>(() => node.HttpClient
				.GetAsync(string.Format("/test-timeout?sleepfor={0}", sleepFor * 1000)),
			message: "The client aborted the request.");
	}
}
