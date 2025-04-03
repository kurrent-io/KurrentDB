// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Http.HealthChecks;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_performing_a_live_check<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node;
	private bool _nodeStarted;
	[SetUp]
	public async Task SetUp() {
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.StartTestServer();
	}

	[TearDown]
	public async Task Teardown() {
		if (_nodeStarted) {
			await _node.Shutdown();
		}
	}

	private static readonly object[] MethodAllowedTestCases = {
		new object[] {HttpMethod.Head},
		new object[] {HttpMethod.Get},
	};

	private static readonly object[] MethodNotAllowedTestCases = typeof(HttpMethod)
		.GetProperties(BindingFlags.Public | BindingFlags.Static)
		.Where(pi => pi.PropertyType == typeof(HttpMethod))
		.Select(pi => (HttpMethod)pi.GetValue(null))
		.Where(x => x != HttpMethod.Get && x != HttpMethod.Head)
		.Select(x => new object[] { x })
		.ToArray();

	[TestCaseSource(nameof(MethodAllowedTestCases))]
	public async Task before_start_returns_error(HttpMethod method) {
		using var response = await _node.HttpClient.SendAsync(new HttpRequestMessage(method, "/health/live"));
		_nodeStarted = false; //just for clarity
		Assert.GreaterOrEqual((int)response.StatusCode, 500);
	}

	[TestCaseSource(nameof(MethodAllowedTestCases))]
	public async Task after_start_returns_success(HttpMethod method) {
		await _node.Start()
			.WithTimeout();
		_nodeStarted = true;
		using var response = await _node.HttpClient.SendAsync(new HttpRequestMessage(method, "/health/live") {
			Version = new Version(2, 0)
		});

		Assert.GreaterOrEqual((int)response.StatusCode, 200);
		Assert.Less((int)response.StatusCode, 400);
	}

	[TestCaseSource(nameof(MethodAllowedTestCases))]
	public async Task with_liveCode_parameter_returns_the_same_liveCode(HttpMethod method) {
		await _node.Start()
			.WithTimeout();
		_nodeStarted = true;
		using var response = await _node.HttpClient.SendAsync(new HttpRequestMessage(method, "/health/live?liveCode=200") {
			Version = new Version(2, 0)
		});

		Assert.AreEqual((int)response.StatusCode, 200);
	}

	[TestCaseSource(nameof(MethodAllowedTestCases))]
	public async Task after_shutdown_returns_error(HttpMethod method) {
		await _node.Start()
			.WithTimeout();
		_nodeStarted = true;
		await _node.Node.StopAsync()
			.WithTimeout();

		using var response = await _node.HttpClient.SendAsync(new HttpRequestMessage(method, "/health/live"));

		Assert.GreaterOrEqual((int)response.StatusCode, 500);
	}

	[TestCaseSource(nameof(MethodNotAllowedTestCases))]
	public async Task using_methods_other_than_get_or_head_returns_method_not_allowed(HttpMethod method) {
		await _node.Start()
			.WithTimeout();
		_nodeStarted = true;
		using var response = await _node.HttpClient.SendAsync(new HttpRequestMessage(method, "/health/live"));

		Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
	}
}
