// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Integration.system_projections;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_recategorizing_events_while_running<TLogFormat, TStreamId> : specification_with_a_v8_query_posted<TLogFormat, TStreamId> {
	protected override void GivenEvents() {
		ExistingEvent("account-01", "test", "", "{\"a\":1}", isJson: true);
		ExistingEvent("account-01", "test", "", "{\"a\":2}", isJson: true);
		ExistingEvent("account-02", "test", "", "{\"a\":10}", isJson: true);
	}

	protected override IEnumerable<WhenStep> When() {
		foreach (var e in base.When()) yield return e;
		yield return CreateWriteEvent("categorized-0", SystemEventTypes.LinkTo, "1@account-01", "{\"a\":2}");
		yield return CreateWriteEvent("categorized-0", SystemEventTypes.LinkTo, "0@account-02", "{\"a\":10}");
		yield return CreateWriteEvent("categorized-1", SystemEventTypes.LinkTo, "0@account-01", "{\"a\":1}");
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
		AssertStreamTail("$category-account", "account-01", "account-02");
		AssertStreamTail("$category-categorized", "categorized-0", "categorized-1");
	}

	[Test]
	public void events_are_categorized() {
		AssertStreamTail("$ce-account", "0@account-01", "1@account-01", "0@account-02");
	}

	[Test]
	public void links_are_categorized() {
		AssertStreamTail("$ce-categorized", "1@account-01", "0@account-02", "0@account-01");
	}
}
