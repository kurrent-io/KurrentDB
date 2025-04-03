// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Integration.system_projections;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_changing_categorization_projection_configurations_to_first<TLogFormat, TStreamId> : specification_with_a_v8_query_posted<TLogFormat, TStreamId> {
	protected override void GivenEvents() {
		ExistingEvent("account-000-01", "test", "", "{\"a\":1}", isJson: true);
		ExistingEvent("account-000-01", "test", "", "{\"a\":2}", isJson: true);
		ExistingEvent("account-000-02", "test", "", "{\"a\":10}", isJson: true);
	}

	protected override IEnumerable<WhenStep> When() {
		foreach (var e in base.When()) yield return e;
		string query = "first\r\n-";
		yield return
			new ProjectionManagementMessage.Command.UpdateQuery(
				Envelope, ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection,
				ProjectionManagementMessage.RunAs.System, query: query, emitEnabled: null);
		yield return
			new ProjectionManagementMessage.Command.UpdateQuery(
				Envelope, ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection,
				ProjectionManagementMessage.RunAs.System, query: query, emitEnabled: null);
		yield return
			new ProjectionManagementMessage.Command.Enable(
				Envelope, ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection,
				ProjectionManagementMessage.RunAs.System);
		yield return
			new ProjectionManagementMessage.Command.Enable(
				Envelope, ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection,
				ProjectionManagementMessage.RunAs.System);
	}

	protected override bool GivenInitializeSystemProjections() {
		return true;
	}

	protected override bool GivenStartSystemProjections() {
		return false;
	}

	protected override string GivenQuery() {
		return "";
	}

	[Test]
	public void streams_are_categorized() {
		AssertStreamTail("$category-account", "account-000-01", "account-000-02");
	}

	[Test]
	public void events_are_categorized() {
		AssertStreamTail("$ce-account", "0@account-000-01", "1@account-000-01", "0@account-000-02");
	}
}
