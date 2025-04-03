// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Transport.Http;
using NUnit.Framework;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Tests.Http.Users.users;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.Streams;


[Category("LongRunning")]
[TestFixture]
public class when_getting_a_stream_without_accept_header : with_admin_user {
	private JObject _descriptionDocument;
	private List<JToken> _links;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		_descriptionDocument = await GetJsonWithoutAcceptHeader<JObject>(TestStream);
	}

	[Test]
	public void returns_not_acceptable() {
		Assert.AreEqual(HttpStatusCode.NotAcceptable, _lastResponse.StatusCode);
	}

	[Test]
	public void returns_a_description_document() {
		Assert.IsNotNull(_descriptionDocument);
		_links = _descriptionDocument != null ? _descriptionDocument["_links"].ToList() : new List<JToken>();
		Assert.IsNotNull(_links, "Expected there to be links in the description but _links is null");
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.DescriptionDocJson)]
[TestFixture(ContentType.LegacyDescriptionDocJson)]
public class when_getting_a_stream_with_description_document_media_type(string contentType) : with_admin_user {
	private JObject _descriptionDocument;
	private List<JToken> _links;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		_descriptionDocument = await GetJson<JObject>(TestStream, contentType, null);
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void returns_a_description_document() {
		Assert.IsNotNull(_descriptionDocument);
		_links = _descriptionDocument != null ? _descriptionDocument["_links"].ToList() : new List<JToken>();
		Assert.IsNotNull(_links, "Expected there to be links in the description but _links is null");
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.DescriptionDocJson)]
[TestFixture(ContentType.LegacyDescriptionDocJson)]
public class when_getting_description_document(string contentType) : with_admin_user {
	private JObject _descriptionDocument;
	private List<JToken> _links;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		_descriptionDocument = await GetJson<JObject>(TestStream, contentType, null);
		_links = _descriptionDocument != null ? _descriptionDocument["_links"].ToList() : new List<JToken>();
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void returns_a_description_document() {
		Assert.IsNotNull(_descriptionDocument);
	}

	[Test]
	public void contains_the_self_link() {
		Assert.AreEqual("self", ((JProperty)_links[0]).Name);
		Assert.AreEqual(TestStream, _descriptionDocument["_links"]["self"]["href"].ToString());
	}

	[Test]
	public void self_link_contains_only_the_description_document_content_type() {
		var supportedContentTypes = _descriptionDocument["_links"]["self"]["supportedContentTypes"].Values<string>()
			.ToArray();
		Assert.AreEqual(1, supportedContentTypes.Length);
		Assert.AreEqual(contentType, supportedContentTypes[0]);
	}

	[Test]
	public void contains_the_stream_link() {
		Assert.AreEqual("stream", ((JProperty)_links[1]).Name);
		Assert.AreEqual(TestStream, _descriptionDocument["_links"]["stream"]["href"].ToString());
	}

	[Test]
	public void stream_link_contains_supported_stream_content_types() {
		var supportedContentTypes = _descriptionDocument["_links"]["stream"]["supportedContentTypes"]
			.Values<string>().ToArray();
		Assert.AreEqual(3, supportedContentTypes.Length);
		Assert.Contains(ContentType.Atom, supportedContentTypes);
		Assert.Contains(ContentType.AtomJson, supportedContentTypes);
		Assert.Contains(ContentType.LegacyAtomJson, supportedContentTypes);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.DescriptionDocJson)]
[TestFixture(ContentType.LegacyDescriptionDocJson)]
public class when_getting_description_document_and_subscription_exists_for_stream(string contentType) : with_admin_user {
	private JObject _descriptionDocument;
	private List<JToken> _links;
	private JToken[] _subscriptions;
	private string _subscriptionUrl;

	protected override async Task Given() {
		_subscriptionUrl = "/subscriptions/" + TestStreamName + "/groupname334";
		await MakeJsonPut(
			_subscriptionUrl,
			new {
				ResolveLinkTos = true
			}, DefaultData.AdminNetworkCredentials);
	}

	protected override async Task When() {
		_descriptionDocument = await GetJson<JObject>(TestStream, contentType, null);
		_links = _descriptionDocument != null ? _descriptionDocument["_links"].ToList() : new List<JToken>();
		_subscriptions = _descriptionDocument["_links"]["streamSubscription"].Values<JToken>().ToArray();
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void returns_a_description_document() {
		Assert.IsNotNull(_descriptionDocument);
	}

	[Test]
	public void contains_3_links() {
		Assert.AreEqual(3, _links.Count);
	}

	[Test]
	public void contains_the_subscription_link() {
		Assert.AreEqual("streamSubscription", ((JProperty)_links[2]).Name);
		Assert.AreEqual(_subscriptionUrl, _subscriptions[0]["href"].ToString());
	}

	[Test]
	public void subscriptions_link_contains_supported_subscription_content_types() {
		var supportedContentTypes = _subscriptions[0]["supportedContentTypes"].Values<string>().ToArray();
		Assert.AreEqual(3, supportedContentTypes.Length);
		Assert.Contains(ContentType.Competing, supportedContentTypes);
		Assert.Contains(ContentType.CompetingJson, supportedContentTypes);
		Assert.Contains(ContentType.LegacyCompetingJson, supportedContentTypes);
	}
}
