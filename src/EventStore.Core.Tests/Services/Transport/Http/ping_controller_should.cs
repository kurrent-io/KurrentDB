// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture, Category("LongRunning")]
public class ping_controller_should : SpecificationWithDirectory {
	private MiniNode<LogFormat.V2,string> _node;

	[OneTimeSetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_node = new MiniNode<LogFormat.V2,string>(PathName);
		await _node.Start();
	}

	[OneTimeTearDown]
	public override async Task TearDown() {
		await base.TearDown();
		await _node.Shutdown();
	}

	[Test]
	public async Task respond_with_httpmessage_text_message() {
		var result = await _node.HttpClient.GetAsync("/ping?format=json");

		Assert.True(result.IsSuccessStatusCode);

		var body = await result.Content.ReadAsStringAsync();
		Assert.NotNull(Codec.Json.From<HttpMessage.TextMessage>(body));
	}

	[Test]
	public async Task return_response_in_json_if_requested_by_query_param_and_set_content_type_header() {
		var result = await _node.HttpClient.GetAsync("/ping?format=json");

		Assert.AreEqual(ContentType.Json, result.Content.Headers.ContentType!.MediaType);
	}

	[Test]
	public async Task return_response_in_xml_if_requested_by_query_param_and_set_content_type_header() {
		var result = await _node.HttpClient.GetAsync("/ping?format=xml");

		Assert.AreEqual(ContentType.Xml, result.Content.Headers.ContentType!.MediaType);
	}

	[Test]
	public async Task return_response_in_plaintext_if_requested_by_query_param_and_set_content_type_header() {
		var result = await _node.HttpClient.GetAsync("/ping?format=text");

		Assert.AreEqual(ContentType.PlainText, result.Content.Headers.ContentType!.MediaType);
	}
}
