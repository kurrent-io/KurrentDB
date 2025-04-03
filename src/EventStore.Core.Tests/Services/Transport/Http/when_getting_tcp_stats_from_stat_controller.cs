// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Transport.Http.Codecs;
using NUnit.Framework;
using EventStore.Core.Tests.ClientAPI;
using HttpMethod = System.Net.Http.HttpMethod;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_getting_tcp_stats_from_stat_controller<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private IEventStoreConnection _connection;
	private string _url;
	private string _clientConnectionName = "test-connection";

	private List<MonitoringMessage.TcpConnectionStats> _results = new List<MonitoringMessage.TcpConnectionStats>();
	private HttpResponseMessage _response;

	protected override async Task Given() {
		_url = _node.HttpEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/stats/tcp");

		var settings = ConnectionSettings.Create().DisableServerCertificateValidation();
		_connection = EventStoreConnection.Create(settings, _node.TcpEndPoint, _clientConnectionName);
		await _connection.ConnectAsync();

		var testEvent = new EventData(Guid.NewGuid(), "TestEvent", true,
			Encoding.ASCII.GetBytes("{'Test' : 'OneTwoThree'}"), null);
		await _connection.AppendToStreamAsync("tests", ExpectedVersion.Any, testEvent);
	}

	protected override async Task When() {
		_response = await _node.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, _url));
		_results = Codec.Json.From<List<MonitoringMessage.TcpConnectionStats>>(
			await _response.Content.ReadAsStringAsync());
	}

	[Test]
	public void should_have_succeeded() {
		Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
	}

	[Test]
	public void should_return_the_external_connections() {
		Assert.AreEqual(2, _results.Count(r => r.IsExternalConnection));
	}

	[Test]
	public void should_return_the_total_number_of_bytes_sent_for_external_connections() {
		Assert.Greater(_results.Sum(r => r.IsExternalConnection ? r.TotalBytesSent : 0), 0);
	}

	[Test]
	public void should_return_the_total_number_of_bytes_received_from_external_connections() {
		Assert.Greater(_results.Sum(r => r.IsExternalConnection ? r.TotalBytesReceived : 0), 0);
	}

	[Test]
	public void should_have_set_the_client_connection_name() {
		Assert.IsTrue(_results.Any(x => x.ClientConnectionName == _clientConnectionName));
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_connection.Dispose();
		return base.TestFixtureTearDown();
	}
}
