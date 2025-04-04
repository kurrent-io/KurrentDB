// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Core.Tests.Http.Info;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_getting_options<TLogFormat, TStreamId> : HttpBehaviorSpecification<TLogFormat, TStreamId> {
	protected override Task Given() => Task.CompletedTask;

	protected override async Task When() {
		await Get("/info/options", "", credentials: DefaultData.AdminNetworkCredentials);
	}

	[Test]
	public void sensitive_options_are_hidden_in_response() {
		Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);

		var options = _lastResponseBody.ParseJson<JArray>();
		var sensitiveOption = options.First(x => x.Value<string>("name") == "DefaultAdminPassword");
		Assert.AreEqual("********", sensitiveOption.Value<string>("value"));
	}
}
