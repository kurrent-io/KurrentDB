// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint.Scenarios;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_running_a_query_using_link_metadata<TLogFormat, TStream> : specification_with_js_query_posted<TLogFormat, TStream> {
	protected override void GivenEvents() {
		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":1}", "0@account-01");
		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":2}", "1@account-01");
		ExistingEvent("stream", SystemEventTypes.LinkTo, "{\"a\":10}", "0@account-02");

		ExistingEvent("account-01", "test", "", "{\"a\":1}", isJson: true);
		ExistingEvent("account-01", "test", "", "{\"a\":2}", isJson: true);
		ExistingEvent("account-02", "test", "", "{\"a\":10}", isJson: true);
	}

	protected override string GivenQuery()
		=> """
		   fromStream('stream').when({
		       $any: function(s, e) {
		           // test
		           if (JSON.stringify(e.body) != JSON.stringify(e.linkMetadata))
		               throw 'invalid link metadata ' + JSON.stringify(e.linkMetadata) + ' expected is ' + JSON.stringify(e.body);

		           return e.linkMetadata;
		       }
		   }).outputState()
		   """;

	[Test]
	public void just() {
		AssertLastEvent("$projections-query-result", "{\"a\":10}", skip: 1 /* $eof */);
	}

	[Test]
	public void state_becomes_completed() {
		_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));

		var actual = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().ToArray();
		Assert.AreEqual(1, actual.Length);
		Assert.AreEqual(1, actual.Single().Projections.Length);
		Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
		Assert.AreEqual(ManagedProjectionState.Completed, actual.Single().Projections.Single().LeaderStatus);
	}
}
