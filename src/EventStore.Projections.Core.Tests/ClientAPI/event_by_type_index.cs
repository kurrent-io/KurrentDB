// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.ClientAPI {
	namespace event_by_type_index {
		public abstract class with_existing_events<TLogFormat, TStreamId> : specification_with_standard_projections_runnning<TLogFormat, TStreamId> {
			protected override async Task Given() {
				await base.Given();
				await PostEvent("stream1", "type1", "{}");
				await PostEvent("stream1", "type2", "{}");
				await PostEvent("stream1", "type3", "{}");
				await PostEvent("stream2", "type1", "{}");
				await PostEvent("stream2", "type2", "{}");
				await PostEvent("stream2", "type3", "{}");
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_creating<TLogFormat, TStreamId> : with_existing_events<TLogFormat, TStreamId> {
			protected override async Task When() {
				await base.When();
				await PostProjection(@"
fromAll().when({
    $init: function(){
        return {c: 0};
    },
    type1: count,
    type2: count
}).outputState()

function count(s,e) {
    return {c: s.c + 1};
}
");
			}

			[Test, Category("Network")]
			public async Task result_is_correct() {
				await AssertStreamTail("$projections-test-projection-result", "Result:{\"c\":4}");
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_posting_more_events<TLogFormat, TStreamId> : with_existing_events<TLogFormat, TStreamId> {
			protected override async Task When() {
				await base.When();
				await PostProjection(@"
fromAll().when({
    $init: function(){
        return {c: 0};
    },
    type1: count,
    type2: count
}).outputState()

function count(s,e) {
    return {c: s.c + 1};
}
");
				await PostEvent("stream3", "type2", "{}");
				await PostEvent("stream3", "type3", "{}");
				WaitIdle();
			}

			[Test, Category("Network")]
			public async Task result_is_correct() {
				await AssertStreamTail("$projections-test-projection-result", "Result:{\"c\":5}");
			}
		}
	}
}
