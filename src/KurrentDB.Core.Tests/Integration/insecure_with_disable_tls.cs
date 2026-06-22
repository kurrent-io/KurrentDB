// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Threading.Tasks;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_running_insecure_with_disable_tls<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture {

	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(
			pathname: PathName,
			disableTls: true,
			insecure: true);
		await _node.Start();
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task unauthenticated_request_succeeds() {
		// In insecure mode, auth is disabled — verify node responds to requests
		var response = await _node.HttpClient.GetAsync("/info");
		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[Test]
	public void node_reports_tls_disabled() {
		Assert.IsTrue(_node.Node.DisableHttps);
	}
}
