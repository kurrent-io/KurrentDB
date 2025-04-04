// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.ClientAPI.when_handling_deleted.with_from_all_foreach_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_running_and_no_indexing<TLogFormat, TStreamId> : specification_with_standard_projections_runnning<TLogFormat, TStreamId> {
	protected override bool GivenStandardProjectionsRunning() {
		return false;
	}

	protected override async Task Given() {
		await base.Given();
		await PostEvent("stream-1", "type1", "{}");
		await PostEvent("stream-1", "type2", "{}");
		await PostEvent("stream-2", "type1", "{}");
		await PostEvent("stream-2", "type2", "{}");
		WaitIdle();
		await PostProjection(@"
fromAll().foreachStream().when({
    $init: function(){return {}},
    type1: function(s,e){s.a=1},
    type2: function(s,e){s.a=1},
    $deleted: function(s,e){s.deleted=1;},
}).outputState();
");
	}

	protected override async Task When() {
		await base.When();
		await HardDeleteStream("stream-1");
		WaitIdle();
	}

	[Test, Category("Network")]
	public async Task receives_deleted_notification() {
		await AssertStreamTail(
			"$projections-test-projection-stream-1-result", "Result:{\"a\":1}", "Result:{\"a\":1,\"deleted\":1}");
	}
}
