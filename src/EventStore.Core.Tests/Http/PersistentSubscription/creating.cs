// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Tests.Http.Users.users;
using NUnit.Framework;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using HttpStatusCode = System.Net.HttpStatusCode;
using HttpResponseMessage = System.Net.Http.HttpResponseMessage;
using Newtonsoft.Json.Linq;
using EventStore.Transport.Http;

namespace EventStore.Core.Tests.Http.PersistentSubscription;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_a_subscription<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname334",
			new {
				ResolveLinkTos = true
			}, _admin);
	}

	[OneTimeTearDown]
	public void TearDown() {
		_response?.Dispose();
	}

	[Test]
	public void returns_created() {
		Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
	}

	[Test]
	public void returns_location_header() {
		Assert.AreEqual("http://" + _node.HttpEndPoint + "/subscriptions/stream/groupname334",
			_response.Headers.Location.ToString());
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_a_subscription_with_query_params<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname334",
			new {
				ResolveLinkTos = true
			}, _admin, "testing=test");
	}

	[OneTimeTearDown]
	public void TearDown() {
		_response.Dispose();
	}

	[Test]
	public void returns_created() {
		Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
	}

	[Test]
	public void returns_location_header() {
		Assert.AreEqual("http://" + _node.HttpEndPoint + "/subscriptions/stream/groupname334",
			_response.Headers.Location.ToString());
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_a_subscription_without_permissions<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		SetDefaultCredentials(null);
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname337",
			new {
				ResolveLinkTos = true
			});
	}

	[OneTimeTearDown]
	public void TearDown() {
		_response?.Dispose();
	}

	[Test]
	public void returns_unauthorised() {
		Assert.AreEqual(HttpStatusCode.Unauthorized, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_a_duplicate_subscription<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override async Task Given() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname453",
			new {
				ResolveLinkTos = true
			}, _admin);
	}

	protected override async Task When() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname453",
			new {
				ResolveLinkTos = true
			}, _admin);
	}

	[OneTimeTearDown]
	public void TearDown() {
		_response?.Dispose();
	}

	[Test]
	public void returns_conflict() {
		Assert.AreEqual(HttpStatusCode.Conflict, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_a_subscription_with_bad_config<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	protected List<object> Events;
	protected string SubscriptionPath;
	protected string GroupName;
	protected HttpResponseMessage Response;

	protected override async Task Given() {
		Events = new List<object> {
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {B = "2"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {C = "3"}},
			new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {D = "4"}}
		};

		Response = await MakeArrayEventsPost(
			TestStream,
			Events,
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, Response.StatusCode);
	}

	protected override async Task When() {
		GroupName = Guid.NewGuid().ToString();
		SubscriptionPath = string.Format("/subscriptions/{0}/{1}", TestStream.Substring(9), GroupName);
		Response = await MakeJsonPut(SubscriptionPath,
			new {
				ResolveLinkTos = true,
				BufferSize = 10,
				ReadBatchSize = 11
			},
			_admin);
	}

	[OneTimeTearDown]
	public void TearDown() {
		Response?.Dispose();
	}

	[Test]
	public void returns_bad_request() {
		Assert.AreEqual(HttpStatusCode.BadRequest, Response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_persistent_subscription_with_message_timeout_0<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	protected string SubscriptionPath;
	protected string GroupName;
	protected HttpResponseMessage Response;
	protected JObject SubsciptionInfo;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		GroupName = Guid.NewGuid().ToString();
		SubscriptionPath = string.Format("/subscriptions/{0}/{1}", TestStream.Substring(9), GroupName);
		Response = await MakeJsonPut(SubscriptionPath,
			new {
				ResolveLinkTos = true,
				MessageTimeoutMilliseconds = 0
			},
			_admin);

		SubsciptionInfo = await GetJson<JObject>(SubscriptionPath + "/info", ContentType.Json);
	}

	[OneTimeTearDown]
	public void TearDown() {
		Response?.Dispose();
	}

	[Test]
	public void returns_created() {
		Assert.AreEqual(HttpStatusCode.Created, Response.StatusCode);

	}

	[Test]
	public void timeout_set_to_0() {
		Assert.AreEqual(0, SubsciptionInfo.Value<JObject>("config").Value<long?>("messageTimeoutMilliseconds"));

	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_creating_persistent_subscription_without_message_timeout<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	protected string SubscriptionPath;
	protected string GroupName;
	protected HttpResponseMessage Response;
	protected JObject SubsciptionInfo;

	protected override Task Given() => Task.CompletedTask;
	protected override async Task When() {
		GroupName = Guid.NewGuid().ToString();
		SubscriptionPath = string.Format("/subscriptions/{0}/{1}", TestStream.Substring(9), GroupName);
		Response = await MakeJsonPut(SubscriptionPath,
			new {
				ResolveLinkTos = true,
			},
			_admin);

		SubsciptionInfo = await GetJson<JObject>(SubscriptionPath + "/info", ContentType.Json);
	}

	[OneTimeTearDown]
	public void TearDown() {
		Response?.Dispose();
	}

	[Test]
	public void returns_created() {
		Assert.AreEqual(HttpStatusCode.Created, Response.StatusCode);

	}

	[Test]
	public void timeout_set_to_default_10000() {
		Assert.AreEqual(10000, SubsciptionInfo.Value<JObject>("config").Value<long?>("messageTimeoutMilliseconds"));

	}
}

