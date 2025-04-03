// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Tests.Http.StreamSecurity;

abstract class SpecificationWithUsers<TLogFormat, TStreamId>
	: HttpBehaviorSpecification<TLogFormat, TStreamId> {
	protected override async Task Given() {
		await PostUser("user1", "User 1", "user1!", "other");
		await PostUser("user2", "User 2", "user2!", "other");
		await PostUser("guest", "Guest", "guest!");
	}

	protected readonly NetworkCredential _admin = DefaultData.AdminNetworkCredentials;

	protected override MiniNode<TLogFormat, TStreamId> CreateMiniNode() {
		return new MiniNode<TLogFormat, TStreamId>(PathName,
			enableTrustedAuth: true);
	}

	protected async Task PostUser(string login, string userFullName, string password, params string[] groups) {
		var response = await MakeJsonPost(
			"/users/", new { LoginName = login + Tag, FullName = userFullName, Groups = groups, Password = password },
			_admin);
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
	}

	protected async Task<string> PostMetadata(StreamMetadata metadata) {
		var response = await MakeArrayEventsPost(
			TestMetadataStream, new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = metadata } });
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
		return response.Headers.GetLocationAsString();
	}

	protected async Task<string> PostEvent(int i) {
		var response = await MakeArrayEventsPost(
			TestStream, new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { Number = i } } });
		Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
		return response.Headers.GetLocationAsString();
	}

	protected Task<HttpResponseMessage> PostEvent<T>(T data, NetworkCredential credentials = null) {
		return MakeArrayEventsPost(
			TestStream, new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = data } }, credentials);
	}

	protected string GetLink(JObject feed, string relation) {
		var rel = (from JObject link in feed["links"]
				   from JProperty attr in link
				   where attr.Name == "relation" && (string)attr.Value == relation
				   select link).SingleOrDefault();
		return (rel == null) ? null : (string)rel["uri"];
	}

	protected NetworkCredential GetCorrectCredentialsFor(string user) {
		return new NetworkCredential(user + Tag, user + "!");
	}
}
