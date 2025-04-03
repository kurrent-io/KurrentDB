// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using EventStore.ClientAPI;
using EventStore.Transport.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Xml.Linq;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Http.Users.users;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.PersistentSubscription;

[Category("LongRunning")]
[TestFixture]
class when_getting_statistics_for_new_subscription_for_stream_with_existing_events : with_subscription_having_events {
	private JArray _json;

	protected override async Task When() {
		_json = await GetJson<JArray>("/subscriptions", accept: ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void should_reflect_the_known_number_of_events_in_the_stream() {
		var knownNumberOfEvents = _json[0]["lastKnownEventNumber"].Value<int>() + 1;
		Assert.AreEqual(Events.Count, knownNumberOfEvents,
			"Expected the subscription statistics to know about {0} events but seeing {1}", Events.Count,
			knownNumberOfEvents);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_statistics_for_subscription_with_parked_events(string contentType) : with_subscription_having_events {
	private string _nackLink;
	private JObject _json;
	private string _streamName;

	private string _subscriptionParkedStream;
	private Guid _writeCorrelationId;
	private TaskCompletionSource<bool> _eventParked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	protected override async Task Given() {
		_connection.Close();
		_connection.Dispose();
		NumberOfEventsToCreate = 1;
		await base.Given();

		_streamName = TestStream.Substring(9);
		_subscriptionParkedStream =
			"$persistentsubscription-" + _streamName + "::" + GroupName + "-parked";

		// Subscribe to the writes to ensure the parked message has been written
		_node.Node.MainBus.Subscribe(new AdHocHandler<StorageMessage.WritePrepares>(Handle));
		_node.Node.MainBus.Subscribe(new AdHocHandler<StorageMessage.CommitIndexed>(Handle));

		var json = await GetJson<JObject>(
			SubscriptionPath + "/1",
			extra: "embed=rich",
			accept: contentType,
			credentials: _admin);

		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
		Assert.DoesNotThrow(() =>
		{
			var _entries = json != null ? json["entries"].ToList() : new List<JToken>();
			_nackLink = _entries[0]["links"][3]["uri"] + "?action=park";
		});

		//Park the message
		var response = await MakePost(_nackLink, _admin);
		Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
	}

	private void Handle(StorageMessage.WritePrepares msg) {
		if (msg.EventStreamId == _subscriptionParkedStream) {
			_writeCorrelationId = msg.CorrelationId;
		}
	}

	private void Handle(StorageMessage.CommitIndexed msg) {
		if (msg.CorrelationId == _writeCorrelationId) {
			_eventParked.TrySetResult(true);
		}
	}

	protected override async Task When() {
		await _eventParked.Task.WithTimeout();
		_json = await GetJson<JObject>("/subscriptions/" + _streamName + "/" + GroupName + "/info", ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void should_show_one_parked_message() {
		Assert.AreEqual(1, _json["parkedMessageCount"].Value<int>());
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_all_statistics_in_json : with_subscription_having_events {
	private JArray _json;

	protected override async Task When() {
		_json = await GetJson<JArray>("/subscriptions", accept: ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void body_contains_valid_json() {
		Assert.AreEqual(TestStreamName, _json[0]["eventStreamId"].Value<string>());
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_all_statistics_in_xml : with_subscription_having_events {
	private XDocument _xml;

	protected override async Task When() {
		_xml = await GetXml(MakeUrl("/subscriptions"));
	}

	[Test]
	[Retry(5)]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void body_contains_valid_xml() {
		Assert.AreEqual(TestStreamName, _xml.Descendants("EventStreamId").First().Value);
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_non_existent_single_statistics : with_admin_user {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		var request = CreateRequest("/subscriptions/fu/fubar", null, "GET", ContentType.Xml);
		_response = await GetRequestResponse(request);
	}

	[Test]
	[Retry(5)]
	public void returns_not_found() {
		Assert.AreEqual(HttpStatusCode.NotFound, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_non_existent_stream_statistics : with_admin_user {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		var request = CreateRequest("/subscriptions/fubar", null, "GET", ContentType.Xml, null);
		_response = await GetRequestResponse(request);
	}

	[Test]
	[Retry(5)]
	public void returns_not_found() {
		Assert.AreEqual(HttpStatusCode.NotFound, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_subscription_statistics_for_individual : SpecificationWithPersistentSubscriptionAndConnections {
	private JObject _json;


	protected override async Task When() {
		_json = await GetJson<JObject>("/subscriptions/" + _streamName + "/" + _groupName + "/info", ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void detail_rel_href_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/subscriptions/{1}/{2}/info", _node.HttpEndPoint, _streamName, _groupName),
			_json["links"][0]["href"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void has_two_rel_links() {
		Assert.AreEqual(2,
			_json["links"].Count());
	}

	[Test]
	[Retry(5)]
	public void the_view_detail_rel_is_correct() {
		Assert.AreEqual("detail",
			_json["links"][0]["rel"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_event_stream_is_correct() {
		Assert.AreEqual(_streamName, _json["eventStreamId"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_groupname_is_correct() {
		Assert.AreEqual(_groupName, _json["groupName"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_status_is_live() {
		Assert.AreEqual("Live", _json["status"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void there_are_two_connections() {
		Assert.AreEqual(2, _json["connections"].Count());
	}

	[Test]
	[Retry(5)]
	public void the_first_connection_has_endpoint() {
		Assert.IsNotNull(_json["connections"][0]["from"]);
	}

	[Test]
	[Retry(5)]
	public void the_second_connection_has_endpoint() {
		Assert.IsNotNull(_json["connections"][1]["from"]);
	}

	[Test]
	[Retry(5)]
	public void the_first_connection_has_user() {
		Assert.AreEqual("admin", _json["connections"][0]["username"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_connection_has_user() {
		Assert.AreEqual("admin", _json["connections"][1]["username"].Value<string>());
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_subscription_stats_summary : SpecificationWithPersistentSubscriptionAndConnections {
	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	private JArray _json;

	protected override async Task Given() {
		await base.Given();
		await _conn.CreatePersistentSubscriptionAsync(_streamName, "secondgroup", _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
	}

	protected override async Task When() {
		_json = await GetJson<JArray>("/subscriptions", ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void the_response_code_is_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_is_correct() {
		Assert.AreEqual(_streamName, _json[0]["eventStreamId"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_groupname_is_correct() {
		Assert.AreEqual(_groupName, _json[0]["groupName"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_detail_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/subscriptions/{1}/{2}/info", _node.HttpEndPoint, _streamName, _groupName),
			_json[0]["links"][0]["href"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_detail_has_one_link() {
		Assert.AreEqual(1,
			_json[0]["links"].Count());
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_detail_rel_is_correct() {
		Assert.AreEqual("detail",
			_json[0]["links"][0]["rel"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_event_stream_detail_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/subscriptions/{1}/{2}/info", _node.HttpEndPoint, _streamName,
				"secondgroup"),
			_json[1]["links"][0]["href"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_event_stream_detail_has_one_link() {
		Assert.AreEqual(1,
			_json[1]["links"].Count());
	}

	[Test]
	[Retry(5)]
	public void the_second_event_stream_detail_rel_is_correct() {
		Assert.AreEqual("detail",
			_json[1]["links"][0]["rel"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_parked_message_queue_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/streams/%24persistentsubscription-{1}::{2}-parked", _node.HttpEndPoint,
				_streamName, _groupName), _json[0]["parkedMessageUri"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_parked_message_queue_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/streams/%24persistentsubscription-{1}::{2}-parked", _node.HttpEndPoint,
				_streamName, "secondgroup"), _json[1]["parkedMessageUri"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_status_is_live() {
		Assert.AreEqual("Live", _json[0]["status"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void there_are_two_connections() {
		Assert.AreEqual(2, _json[0]["connectionCount"].Value<int>());
	}

	[Test]
	[Retry(5)]
	public void the_second_subscription_event_stream_is_correct() {
		Assert.AreEqual(_streamName, _json[1]["eventStreamId"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_subscription_groupname_is_correct() {
		Assert.AreEqual("secondgroup", _json[1]["groupName"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void second_subscription_there_are_three_connections() {
		Assert.AreEqual(3, _json[1]["connectionCount"].Value<int>());
	}
}

[Category("LongRunning")]
[TestFixture]
class when_getting_subscription_statistics_for_stream : SpecificationWithPersistentSubscriptionAndConnections {
	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	private JArray _json;
	private EventStorePersistentSubscriptionBase _sub4;
	private EventStorePersistentSubscriptionBase _sub3;
	private EventStorePersistentSubscriptionBase _sub5;

	protected override async Task Given() {
		await base.Given();
		await _conn.CreatePersistentSubscriptionAsync(_streamName, "secondgroup", _settings,
			DefaultData.AdminCredentials);
		_sub3 = _conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(), DefaultData.AdminCredentials);
		_sub4 = _conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
		_sub5 = _conn.ConnectToPersistentSubscription(_streamName, "secondgroup",
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
	}

	protected override async Task When() {
		//make mcs stop bitching
		Console.WriteLine(_sub3);
		Console.WriteLine(_sub4);
		Console.WriteLine(_sub5);
		_json = await GetJson<JArray>("/subscriptions/" + _streamName, ContentType.Json);
	}

	[Test]
	[Retry(5)]
	public void the_response_code_is_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_is_correct() {
		Assert.AreEqual(_streamName, _json[0]["eventStreamId"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_groupname_is_correct() {
		Assert.AreEqual(_groupName, _json[0]["groupName"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_event_stream_detail_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/subscriptions/{1}/{2}/info", _node.HttpEndPoint, _streamName, _groupName),
			_json[0]["links"][0]["href"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_event_stream_detail_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/subscriptions/{1}/{2}/info", _node.HttpEndPoint, _streamName,
				"secondgroup"),
			_json[1]["links"][0]["href"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_first_parked_message_queue_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/streams/%24persistentsubscription-{1}::{2}-parked", _node.HttpEndPoint,
				_streamName, _groupName), _json[0]["parkedMessageUri"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_parked_message_queue_uri_is_correct() {
		Assert.AreEqual(
			string.Format("http://{0}/streams/%24persistentsubscription-{1}::{2}-parked", _node.HttpEndPoint,
				_streamName, "secondgroup"), _json[1]["parkedMessageUri"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_status_is_live() {
		Assert.AreEqual("Live", _json[0]["status"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void there_are_two_connections() {
		Assert.AreEqual(2, _json[0]["connectionCount"].Value<int>());
	}

	[Test]
	[Retry(5)]
	public void the_second_subscription_event_stream_is_correct() {
		Assert.AreEqual(_streamName, _json[1]["eventStreamId"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void the_second_subscription_groupname_is_correct() {
		Assert.AreEqual("secondgroup", _json[1]["groupName"].Value<string>());
	}

	[Test]
	[Retry(5)]
	public void second_subscription_there_are_three_connections() {
		Assert.AreEqual(3, _json[1]["connectionCount"].Value<int>());
	}
}

public abstract class SpecificationWithPersistentSubscriptionAndConnections : with_admin_user {
	protected string _streamName = Guid.NewGuid().ToString();
	protected string _groupName = Guid.NewGuid().ToString();
	protected IEventStoreConnection _conn;
	protected EventStorePersistentSubscriptionBase _sub1;
	protected EventStorePersistentSubscriptionBase _sub2;

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override async Task Given() {
		_conn = EventStoreConnection.Create(ConnectionSettings.Create().DisableServerCertificateValidation().Build(),
			_node.TcpEndPoint);
		await _conn.ConnectAsync();
		await _conn.CreatePersistentSubscriptionAsync(_streamName, _groupName, _settings,
			DefaultData.AdminCredentials);
		_sub1 = _conn.ConnectToPersistentSubscription(_streamName, _groupName,
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(), DefaultData.AdminCredentials);
		_sub2 = _conn.ConnectToPersistentSubscription(_streamName, _groupName,
			(subscription, @event) => {
				Console.WriteLine();
				return Task.CompletedTask;
			},
			(subscription, reason, arg3) => Console.WriteLine(),
			DefaultData.AdminCredentials);
	}

	protected override Task When() => Task.CompletedTask;

	[OneTimeTearDown]
	public async Task Teardown() {
		await _conn.DeletePersistentSubscriptionAsync(_streamName, _groupName, DefaultData.AdminCredentials);
		_conn.Close();
		_conn.Dispose();
	}
}
