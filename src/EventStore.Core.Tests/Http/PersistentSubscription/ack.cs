// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HttpStatusCode = System.Net.HttpStatusCode;
using EventStore.Transport.Http;

// ReSharper disable InconsistentNaming

namespace EventStore.Core.Tests.Http.PersistentSubscription;

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_acking_a_message(string contentType) : with_subscription_having_events {
	private HttpResponseMessage _response;
	private string _ackLink;

	protected override async Task Given() {
		await base.Given();
		var json = await GetJson<JObject>(
			SubscriptionPath + "/1",
			contentType,
			_admin);
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
		_ackLink = json["entries"].Children().First()["links"].Children()
			.First(x => x.Value<string>("relation") == "ack").Value<string>("uri");
	}

	[TearDown]
	public void TearDown() {
		_response.Dispose();
	}

	protected override async Task When() {
		_response = await MakePost(_ackLink, _admin);
	}

	[Test]
	public void returns_accepted() {
		Assert.AreEqual(HttpStatusCode.Accepted, _response.StatusCode);
	}
}

[TestFixture(ContentType.CompetingJson)]
[TestFixture(ContentType.LegacyCompetingJson)]
class when_acking_messages(string contentType) : with_subscription_having_events {
	private HttpResponseMessage _response;
	private string _ackAllLink;

	protected override async Task Given() {
		await base.Given();
		var json = await GetJson<JObject>(
			SubscriptionPath + "/" + Events.Count,
			contentType,
			_admin);
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
		_ackAllLink = json["links"].Children().First(x => x.Value<string>("relation") == "ackAll")
			.Value<string>("uri");
	}

	[TearDown]
	public void TearDown() {
		_response.Dispose();
	}

	protected override async Task When() {
		_response = await MakePost(_ackAllLink, _admin);
	}

	[Test]
	public void returns_accepted() {
		Assert.AreEqual(HttpStatusCode.Accepted, _response.StatusCode);
	}
}
