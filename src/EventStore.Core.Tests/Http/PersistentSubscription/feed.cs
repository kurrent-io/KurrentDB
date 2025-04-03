// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Xml.Linq;
using EventStore.Core.Tests.Helpers;
using EventStore.Transport.Http;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Tests.Http.Streams;
using EventStore.Core.Tests.Http.Users.users;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.PersistentSubscription;

abstract class SpecificationWithLongFeed : with_admin_user {
	protected int _numberOfEvents = 5;

	protected string SubscriptionGroupName {
		get { return "test_subscription_group" + Tag; }
	}

	protected string _subscriptionEndpoint;
	protected string _subscriptionStream;
	protected string _subscriptionGroupName;
	protected List<Guid> _eventIds = new List<Guid>();

	protected async Task SetupPersistentSubscription(string streamId, string groupName, int messageTimeoutInMs = 10000) {
		_subscriptionStream = streamId;
		_subscriptionGroupName = groupName;
		_subscriptionEndpoint =
			String.Format("/subscriptions/{0}/{1}", _subscriptionStream, _subscriptionGroupName);

		var response = await MakeJsonPut(
			_subscriptionEndpoint,
			new {
				ResolveLinkTos = true,
				MessageTimeoutMilliseconds = messageTimeoutInMs
			}, _admin);

		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
	}

	protected async Task<string> PostEvent(int i) {
		var eventId = Guid.NewGuid();
		var response = await MakeArrayEventsPost(
			TestStream, new[] { new { EventId = eventId, EventType = "event-type", Data = new { Number = i } } });
		_eventIds.Add(eventId);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
		return response.Headers.Location.ToString();
	}

	protected override async Task Given() {
		await SetupPersistentSubscription(TestStreamName, SubscriptionGroupName);
		for (var i = 0; i < _numberOfEvents; i++) {
			await PostEvent(i);
		}
	}

	protected string GetLink(JObject feed, string relation) {
		var rel = (from JObject link in feed["links"]
				   from JProperty attr in link
				   where attr.Name == "relation" && (string)attr.Value == relation
				   select link).SingleOrDefault();
		return (rel == null) ? (string)null : (string)rel["uri"];
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_retrieving_an_empty_feed(string contentType) : SpecificationWithLongFeed {
	private JObject _feed;
	private JObject _head;
	private string _previous;

	protected override async Task Given() {
		await base.Given();
		_head = await GetJson<JObject>(_subscriptionEndpoint + "/" + _numberOfEvents, contentType);
		_previous = GetLink(_head, "previous");
	}

	protected override async Task When() {
		_feed = await GetJson<JObject>(_previous, contentType);
	}

	[Test]
	public void returns_ok_status_code() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void does_not_contain_ack_all_link() {
		var rel = GetLink(_feed, "ackAll");
		Assert.That(string.IsNullOrEmpty(rel));
	}

	[Test]
	public void does_not_contain_nack_all_link() {
		var rel = GetLink(_feed, "nackAll");
		Assert.That(string.IsNullOrEmpty(rel));
	}

	[Test]
	public void contains_a_link_rel_previous() {
		var rel = GetLink(_feed, "previous");
		Assert.That(!string.IsNullOrEmpty(rel));
	}

	[Test]
	public void the_feed_is_empty() {
		Assert.AreEqual(0, _feed["entries"].Count());
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_retrieving_a_feed_with_events(string contentType) : SpecificationWithLongFeed {
	private JObject _feed;
	private List<JToken> _entries;

	protected override async Task When() {
		var allMessagesFeedLink = String.Format("{0}/{1}", _subscriptionEndpoint, _numberOfEvents);
		_feed = await GetJson<JObject>(allMessagesFeedLink, contentType);
		_entries = _feed != null ? _feed["entries"].ToList() : new List<JToken>();
	}

	[Test]
	public void returns_ok_status_code() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void contains_all_the_events() {
		Assert.AreEqual(_numberOfEvents, _entries.Count);
	}

	[Test]
	public void the_ackAll_link_is_to_correct_uri() {
		var ids = String.Format("ids={0}", String.Join(",", _eventIds.ToArray()));
		var ackAllLink = String.Format("subscriptions/{0}/{1}/ack", TestStreamName, SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(ackAllLink, ids), GetLink(_feed, "ackAll"));
	}

	[Test]
	public void the_nackAll_link_is_to_correct_uri() {
		var ids = String.Format("ids={0}", String.Join(",", _eventIds.ToArray()));
		var nackAllLink = String.Format("subscriptions/{0}/{1}/nack", TestStreamName, SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(nackAllLink, ids), GetLink(_feed, "nackAll"));
	}

	[Test]
	public void all_entries_have_retry_count_element() {
		var allEntriesHaveRetryCount = _feed["entries"].All(entry => entry["retryCount"] != null);
		Assert.True(allEntriesHaveRetryCount);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_polling_the_head_forward_and_a_new_event_appears(string contentType) : SpecificationWithLongFeed {
	private JObject _feed;
	private JObject _head;
	private string _previous;
	private string _lastEventLocation;
	private List<JToken> _entries;

	protected override async Task Given() {
		await base.Given();
		_head = await GetJson<JObject>(_subscriptionEndpoint + "/" + _numberOfEvents, contentType);
		_previous = GetLink(_head, "previous");
		_lastEventLocation = await PostEvent(-1);
	}

	protected override async Task When() {
		_feed = await GetJson<JObject>(_previous, contentType);
		_entries = _feed != null ? _feed["entries"].ToList() : new List<JToken>();
	}

	[Test]
	public void returns_ok_status_code() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void returns_a_feed_with_a_single_entry_referring_to_the_last_event() {
		HelperExtensions.AssertJson(new { entries = new[] { new { Id = _lastEventLocation } } }, _feed);
	}

	[Test]
	public void the_ack_link_is_to_correct_uri() {
		var link = _entries[0]["links"][2];
		Assert.AreEqual("ack", link["relation"].ToString());
		var ackLink = String.Format("subscriptions/{0}/{1}/ack/{2}", TestStreamName, SubscriptionGroupName,
			_eventIds.Last());
		Assert.AreEqual(MakeUrl(ackLink), link["uri"].ToString());
	}

	[Test]
	public void the_nack_link_is_to_correct_uri() {
		var link = _entries[0]["links"][3];
		Assert.AreEqual("nack", link["relation"].ToString());
		var ackLink = String.Format("subscriptions/{0}/{1}/nack/{2}", TestStreamName, SubscriptionGroupName,
			_eventIds.Last());
		Assert.AreEqual(MakeUrl(ackLink), link["uri"].ToString());
	}
}

[Category("LongRunning")]
[TestFixture]
class when_retrieving_a_feed_with_events_with_competing_xml : SpecificationWithLongFeed {
	private XDocument document;
	private XElement[] _entries;

	protected override async Task When() {
		await Get(MakeUrl(_subscriptionEndpoint + "/" + 1).ToString(), String.Empty, ContentType.Competing);
		document = XDocument.Parse(_lastResponseBody);
		_entries = document.GetEntries();
	}

	[Test]
	public void the_feed_has_n_events() {
		Assert.AreEqual(1, _entries.Length);
	}

	[Test]
	public void contains_all_the_events() {
		Assert.AreEqual(1, _entries.Length);
	}

	[Test]
	public void the_ackAll_link_is_to_correct_uri() {
		var ids = String.Format("ids={0}", _eventIds[0]);
		var ackAllLink = String.Format("subscriptions/{0}/{1}/ack", TestStreamName, SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(ackAllLink, ids),
			document.Element(XDocumentAtomExtensions.AtomNamespace + "feed").GetLink("ackAll"));
	}

	[Test]
	public void the_nackAll_link_is_to_correct_uri() {
		var ids = String.Format("ids={0}", _eventIds[0]);
		var nackAllLink = String.Format("subscriptions/{0}/{1}/nack", TestStreamName, SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(nackAllLink, ids),
			document.Element(XDocumentAtomExtensions.AtomNamespace + "feed").GetLink("nackAll"));
	}

	[Test]
	public void the_ack_link_is_to_correct_uri() {
		var result = document.Element(XDocumentAtomExtensions.AtomNamespace + "feed")
			.Element(XDocumentAtomExtensions.AtomNamespace + "entry")
			.GetLink("ack");
		var ackLink = String.Format("subscriptions/{0}/{1}/ack/{2}", TestStreamName, SubscriptionGroupName,
			_eventIds[0]);
		Assert.AreEqual(MakeUrl(ackLink), result);
	}

	[Test]
	public void the_nack_link_is_to_correct_uri() {
		var result = document.Element(XDocumentAtomExtensions.AtomNamespace + "feed")
			.Element(XDocumentAtomExtensions.AtomNamespace + "entry")
			.GetLink("nack");
		;
		var nackLink = String.Format("subscriptions/{0}/{1}/nack/{2}", TestStreamName, SubscriptionGroupName,
			_eventIds[0]);
		Assert.AreEqual(MakeUrl(nackLink), result);
	}
}

[Category("LongRunning")]
[TestFixture]
class when_retrieving_a_feed_with_invalid_content_type : SpecificationWithLongFeed {
	protected override Task When() {
		return Get(MakeUrl(_subscriptionEndpoint + "/" + _numberOfEvents).ToString(), String.Empty, ContentType.Xml);
	}

	[Test]
	public void returns_not_acceptable() {
		Assert.AreEqual(HttpStatusCode.NotAcceptable, _lastResponse.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_retrieving_a_feed_with_events_using_prefix(string contentType) : SpecificationWithLongFeed {
	private JObject _feed;
	private List<JToken> _entries;
	private string _prefix;

	protected override async Task When() {
		_prefix = "myprefix";
		var headers = new NameValueCollection();
		headers.Add("X-Forwarded-Prefix", _prefix);
		var allMessagesFeedLink = String.Format("{0}/{1}", _subscriptionEndpoint, _numberOfEvents);
		_feed = await GetJson<JObject>(allMessagesFeedLink, contentType, headers: headers);
		_entries = _feed != null ? _feed["entries"].ToList() : new List<JToken>();
	}

	[Test]
	public void returns_ok_status_code() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void contains_all_the_events() {
		Assert.AreEqual(_numberOfEvents, _entries.Count);
	}

	[Test]
	public void contains_previous_link_with_prefix() {
		var previousLink = String.Format("{0}/subscriptions/{1}/{2}/5", _prefix, TestStreamName,
			SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(previousLink), GetLink(_feed, "previous"));
	}

	[Test]
	public void contains_self_link_with_prefix() {
		var selfLink = String.Format("{0}/subscriptions/{1}/{2}", _prefix, TestStreamName, SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(selfLink), GetLink(_feed, "self"));
	}

	[Test]
	public void the_ackAll_link_is_to_correct_uri_with_prefix() {
		var ids = String.Format("ids={0}", String.Join(",", _eventIds.ToArray()));
		var ackAllLink = String.Format("{0}/subscriptions/{1}/{2}/ack", _prefix, TestStreamName,
			SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(ackAllLink, ids), GetLink(_feed, "ackAll"));
	}

	[Test]
	public void the_nackAll_link_is_to_correct_uri_with_prefix() {
		var ids = String.Format("ids={0}", String.Join(",", _eventIds.ToArray()));
		var nackAllLink = String.Format("{0}/subscriptions/{1}/{2}/nack", _prefix, TestStreamName,
			SubscriptionGroupName);
		Assert.AreEqual(MakeUrl(nackAllLink, ids), GetLink(_feed, "nackAll"));
	}
}
