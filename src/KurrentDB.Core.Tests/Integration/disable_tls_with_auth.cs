// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_running_with_disable_tls<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture {

	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(
			pathname: PathName,
			disableTls: true);
		await _node.Start();
		await _node.AdminUserCreated;
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task unauthenticated_request_is_rejected() {
		// The node's built-in HttpClient has no credentials — use it directly
		// to verify that unauthenticated access to a protected resource is rejected
		var response = await _node.HttpClient.GetAsync("/streams/$all");
		Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Test]
	public async Task authenticated_request_succeeds() {
		using var request = new HttpRequestMessage(HttpMethod.Get, "/streams/$all");
		request.Headers.Authorization = new AuthenticationHeaderValue(
			"Basic",
			Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:changeit")));

		var response = await _node.HttpClient.SendAsync(request);
		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[Test]
	public void node_reports_tls_disabled() {
		Assert.IsTrue(_node.Node.DisableHttps);
	}
}
