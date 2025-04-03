// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Tests.Http.Users.users;
using NUnit.Framework;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using HttpStatusCode = System.Net.HttpStatusCode;
using EventStore.Transport.Http;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;

// ReSharper disable InconsistentNaming

namespace EventStore.Core.Tests.Http.PersistentSubscription;

abstract class with_subscription_having_events : with_admin_user {
	protected List<object> Events;
	protected string SubscriptionPath;
	protected string GroupName;
	protected int? NumberOfEventsToCreate;

	protected override async Task Given() {
		Events = new List<object> {
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {B = "2"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {C = "3"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {D = "4"}}
		};

		var numberOfEventsToCreate = NumberOfEventsToCreate ?? Events.Count;
		var response = await MakeArrayEventsPost(
			TestStream,
			Events.Take(numberOfEventsToCreate),
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

		GroupName = Guid.NewGuid().ToString();
		SubscriptionPath = string.Format("/subscriptions/{0}/{1}", TestStream.Substring(9), GroupName);
		response = await MakeJsonPut(SubscriptionPath,
			new {
				ResolveLinkTos = true,
				MessageTimeoutMilliseconds = 10000,
			},
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
		AssertEx.IsOrBecomesTrue( () => {
			var x = GetJson<JObject>(TestStream, accept: ContentType.Json).Result;
			return x["entries"].Count() == numberOfEventsToCreate;
		});
	}

	protected override Task When() => Task.CompletedTask;

	protected async Task SecureStream() {
		var metadata =
			(StreamMetadata)
			StreamMetadata.Build()
				.SetMetadataReadRole("admin")
				.SetMetadataWriteRole("admin")
				.SetReadRole("admin")
				.SetWriteRole("admin");
		var jsonMetadata = metadata.AsJsonString();
		var response = await MakeArrayEventsPost(
			TestMetadataStream,
			new[] {
				new {
					EventId = Guid.NewGuid(),
					EventType = SystemEventTypes.StreamMetadata,
					Data = new JRaw(jsonMetadata)
				}
			});
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_without_permission(string contentType) : with_subscription_having_events {
	protected override async Task Given() {
		await base.Given();
		await SecureStream();
	}

	protected override async Task When() {
		SetDefaultCredentials(null);
		await GetJson<JObject>(
			SubscriptionPath,
			contentType);
	}

	[Test]
	public void returns_unauthorised() {
		Assert.AreEqual(HttpStatusCode.Unauthorized, _lastResponse.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_an_empty_subscription(string contentType) : with_admin_user {
	private JObject _response;
	protected List<object> Events;
	protected string SubscriptionPath;
	protected string GroupName;

	protected override async Task Given() {
		Events = new List<object> {
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {B = "2"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {C = "3"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {D = "4"}}
		};

		GroupName = Guid.NewGuid().ToString();
		SubscriptionPath = string.Format("/subscriptions/{0}/{1}", TestStream.Substring(9), GroupName);
		var response = await MakeJsonPut(SubscriptionPath,
			new {
				ResolveLinkTos = true,
				MessageTimeoutMilliseconds = 10000
			},
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

		response = await MakeArrayEventsPost(
			TestStream,
			Events,
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

		//pull all events out.
		_response = await GetJson<JObject>(
			SubscriptionPath + "/" + Events.Count,
			contentType,
			_admin);

		var count = _response["entries"].Count();
		Assert.AreEqual(Events.Count, count, "Expected {0} events, received {1}", Events.Count, count);
	}

	protected override async Task When() {
		_response = await GetJson<JObject>(
			SubscriptionPath,
			contentType,
			_admin);
	}

	[Test]
	public void return_0_messages() {
		var count = _response["entries"].Count();
		Assert.AreEqual(0, count, "Expected {0} events, received {1}", 0, count);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_n_messages(string contentType) : with_subscription_having_events {
	private JObject _response;

	protected override async Task When() {
		_response = await GetJson<JObject>(
			SubscriptionPath + "/" + Events.Count,
			contentType,
			_admin);
	}

	[Test]
	public void returns_n_messages() {
		var count = ((JObject)_response)["entries"].Count();
		Assert.AreEqual(Events.Count, count, "Expected {0} events, received {1}", Events.Count, count);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_more_than_n_messages(string contentType) : with_subscription_having_events {
	private JObject _response;

	protected override async Task When() {
		_response = await GetJson<JObject>(
			SubscriptionPath + "/" + (Events.Count - 1),
			contentType,
			_admin);
	}

	[Test]
	public void returns_n_messages() {
		var count = ((JArray)_response["entries"]).Count;
		Assert.AreEqual(Events.Count - 1, count, "Expected {0} events, received {1}", Events.Count - 1, count);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_less_than_n_messags(string contentType) : with_subscription_having_events {
	private JObject _response;

	protected override async Task When() {
		_response = await GetJson<JObject>(
			SubscriptionPath + "/" + (Events.Count + 1),
			contentType,
			_admin);
	}

	[Test]
	public void returns_all_messages() {
		var count = ((JArray)_response["entries"]).Count;
		Assert.AreEqual(Events.Count, count, "Expected {0} events, received {1}", Events.Count, count);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_unspecified_count(string contentType) : with_subscription_having_events {
	private JObject _response;

	protected override async Task When() {
		_response = await GetJson<JObject>(
			SubscriptionPath,
			contentType,
			_admin);
	}

	[Test]
	public void returns_1_message() {
		var count = ((JArray)_response["entries"]).Count;
		Assert.AreEqual(1, count, "Expected {0} events, received {1}", 1, count);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_a_negative_count(string contentType) : with_subscription_having_events {
	protected override Task When() {
		return Get(SubscriptionPath + "/-1",
			"",
			contentType,
			_admin);
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_a_count_of_0(string contentType) : with_subscription_having_events {
	protected override async Task When() {
		await Get(SubscriptionPath + "/0",
			"",
			contentType,
			_admin);
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_count_more_than_100(string contentType) : with_subscription_having_events {
	protected override async Task When() {
		await Get(SubscriptionPath + "/101",
			"",
			contentType,
			_admin);
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_count_not_an_integer(string contentType) : with_subscription_having_events {
	protected override Task When() {
		return Get(SubscriptionPath + "/10.1",
			"",
			contentType,
			_admin);
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_getting_messages_from_a_subscription_with_count_not_a_number(string contentType) : with_subscription_having_events {
	protected override Task When() {
		return Get(SubscriptionPath + "/one",
			"",
			contentType,
			_admin);
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}
}
