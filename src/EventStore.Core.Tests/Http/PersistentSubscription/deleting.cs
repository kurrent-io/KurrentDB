// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.Http.Users.users;
using NUnit.Framework;

namespace EventStore.Core.Tests.Http.PersistentSubscription;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_deleting_non_existing_subscription<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		var req = CreateRequest("/subscriptions/stream/groupname158", "DELETE", _admin);
		_response = await GetRequestResponse(req);
	}

	[Test]
	public void returns_not_found() {
		Assert.AreEqual(HttpStatusCode.NotFound, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_deleting_an_existing_subscription<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override async Task Given() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname156",
			new {
				ResolveLinkTos = true
			}, _admin);
	}

	protected override async Task When() {
		var req = CreateRequest("/subscriptions/stream/groupname156", "DELETE", _admin);
		_response = await GetRequestResponse(req);
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_deleting_an_existing_subscription_without_permissions<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;

	protected override async Task Given() {
		_response = await MakeJsonPut(
			"/subscriptions/stream/groupname156",
			new {
				ResolveLinkTos = true
			}, _admin);
	}

	protected override async Task When() {
		SetDefaultCredentials(null);
		var req = CreateRequest("/subscriptions/stream/groupname156", "DELETE");
		_response = await GetRequestResponse(req);
	}

	[Test]
	public void returns_unauthorized() {
		Assert.AreEqual(HttpStatusCode.Unauthorized, _response.StatusCode);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_deleting_an_existing_subscription_with_subscribers<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
	private HttpResponseMessage _response;
	private const string _stream = "astreamname";
	private readonly string _groupName = Guid.NewGuid().ToString();
	private SubscriptionDropReason _reason;
	private Exception _exception;
	private readonly AutoResetEvent _dropped = new AutoResetEvent(false);

	protected override async Task Given() {
		_response = await MakeJsonPut(
			string.Format("/subscriptions/{0}/{1}", _stream, _groupName),
			new {
				ResolveLinkTos = true
			}, _admin);
		_connection.ConnectToPersistentSubscription(_stream, _groupName, (x, y) => Task.CompletedTask,
			(sub, reason, e) => {
				_dropped.Set();
				_reason = reason;
				_exception = e;
			}, DefaultData.AdminCredentials);
	}

	protected override async Task When() {
		var req = CreateRequest(string.Format("/subscriptions/{0}/{1}", _stream, _groupName), "DELETE", _admin);
		_response = await GetRequestResponse(req);
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
	}

	[Test]
	public void the_subscription_is_dropped() {
		Assert.IsTrue(_dropped.WaitOne(TimeSpan.FromSeconds(5)));
		Assert.AreEqual(SubscriptionDropReason.UserInitiated, _reason);
		Assert.IsNull(_exception);
	}
}
