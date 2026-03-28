// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Http.Info;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_getting_info<TLogFormat, TStreamId> : HttpBehaviorSpecification<TLogFormat, TStreamId> {
	private JObject _response;

	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		await Get("/info", "");
		_response = _lastResponseBody.ParseJson<JObject>();
	}

	[Test]
	public void returns_ok() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
	}

	[Test]
	public void response_contains_cluster_id() {
		var clusterId = _response["clusterId"];
		Assert.That(clusterId, Is.Not.Null, "clusterId should be present in /info response");
		Assert.That(clusterId.Type, Is.EqualTo(JTokenType.String));

		var parsed = Guid.TryParse(clusterId.Value<string>(), out var guid);
		Assert.That(parsed, Is.True, "clusterId should be a valid GUID");
		Assert.That(guid, Is.Not.EqualTo(Guid.Empty));
	}
}
