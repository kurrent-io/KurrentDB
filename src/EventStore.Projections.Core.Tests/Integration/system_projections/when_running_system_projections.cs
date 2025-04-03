// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Integration.system_projections;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_running_system_projections<TLogFormat, TStreamId> : specification_with_a_v8_query_posted<TLogFormat, TStreamId> {
	protected override void GivenEvents() {
		ExistingEvent("account-01", "test", "", "{\"a\":1}", isJson: true);
		ExistingEvent("account-01", "test", "", "{\"a\":2}", isJson: true);
		ExistingEvent("account-02", "test", "", "{\"a\":10}", isJson: true);
		ExistingEvent("account-000-02", "test", "", "{\"a\":10}", isJson: true);

		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":1}", "0@account-01");
		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":2}", "1@account-01");
		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":10}", "0@account-02");

		ExistingEvent("stream-1", SystemEventTypes.LinkTo, "{\"a\":10}", "1@account-01");
	}

	protected override IEnumerable<WhenStep> When() {
		foreach (var e in base.When()) yield return e;
		yield return CreateWriteEvent("test-1", "test1", "{}", "{}", isJson: true);
		yield return CreateWriteEvent("test-2", SystemEventTypes.LinkTo, "0@test-1", "{}", isJson: true);
	}

	protected override bool GivenInitializeSystemProjections() {
		return true;
	}

	protected override bool GivenStartSystemProjections() {
		return true;
	}

	protected override string GivenQuery() {
		return "";
	}

	[Test]
	public void streams_are_categorized() {
		AssertStreamTail("$category-stream", "stream-1");
		AssertStreamTail("$category-test", "test-1", "test-2");
		AssertStreamTail("$category-account", "account-01", "account-02", "account-000-02");
	}

	[Test]
	public void streams_are_indexed() {
		AssertStreamContains(
			"$streams", "0@account-01", "0@account-02", "0@stream", "0@test-1", "0@test-2", "0@stream-1",
			"0@account-000-02");
	}

	[Test]
	public void events_are_categorized() {
		AssertStreamTail("$ce-stream", "1@account-01");
		AssertStreamTail("$ce-test", "0@test-1", "0@test-1");
		AssertStreamTail("$ce-account", "0@account-01", "1@account-01", "0@account-02", "0@account-000-02");
	}
}
