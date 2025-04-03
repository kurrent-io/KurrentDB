// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint.Scenarios;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(long))]
public class when_recategorizing_chat_events_by_users<TLogFormat, TStream> : specification_with_js_query_posted<TLogFormat, TStream> {
	protected override void GivenEvents() {
	}

	protected override IEnumerable<WhenStep> When() {
		foreach (var e in base.When()) yield return e;
		yield return CreateWriteEvent("chat-1", "ChatMessage", @"
    {
      ""sender"": ""Greg"",
      ""message"": ""Hi"",
      ""time"": ""03:45:30""
    }");
		yield return CreateWriteEvent("chat-1", "ChatMessage", @"
    {
      ""sender"": ""Ronan"",
      ""message"": ""starbucks"",
      ""time"": ""03:45:31""
    }");
		yield return CreateWriteEvent("chat-1", "ChatMessage", @"
    {
      ""sender"": ""James"",
      ""message"": ""herpherp"",
      ""time"": ""03:45:32""
    }");
		yield return CreateWriteEvent("chat-2", "ChatMessage", @"
    {
      ""sender"": ""Rob"",
      ""message"": ""starbucks"",
      ""time"": ""03:45:30""
    }");
		yield return CreateWriteEvent("chat-2", "ChatMessage", @"
    {
      ""sender"": ""Ronan"",
      ""message"": ""put the moose in the chocolate"",
      ""time"": ""03:45:31""
    }");
		yield return CreateQueryMessage("query1", @"
fromCategory(""chat"")
    .foreachStream()
    .when({
        $init: function() {
            return { count: 0 }
        },
        ChatMessage: function(state, event) {
            state.count += 1;
            return state;
        }
    });
");
		yield return CreateQueryMessage("query2", @"
fromCategory(""user"")
    .foreachStream()
    .when({
        $init: function() {
            return { count: 0 }
        },
        ChatMessage: function(state, event) {
            state.count += 1;
            return state;
        }
    });
");
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

	protected override IEnumerable<string> GivenOtherProjections() {
		yield return @"
fromCategory(""chat"")
.when({
    ChatMessage: function(state, event) {
        var userStream = ""user-"" + event.body.sender;
        linkTo(userStream, event);
    }
})";

		yield return @"
fromCategory(""user"")
    .foreachStream()
    .when({
        $init: function() {
            return { count: 0 }
        },
        ChatMessage: function(state, event) {
            state.count += 1;
            return state;
        }
    }).outputState();
";
	}

	[Test]
	public void query1_returns_correct_result() {
		AssertStreamTailWithLinks(
			"$projections-query1-result", @"Result:{""count"":3}", @"Result:{""count"":2}", "$Eof:");
	}

	[Test]
	public void query2_returns_correct_result() {
		AssertStreamTailWithLinks(
			"$projections-query2-result", @"Result:{""count"":1}", @"Result:{""count"":2}", @"Result:{""count"":1}",
			@"Result:{""count"":1}", "$Eof:");
	}

	[Test]
	public void other_1_projection_produces_correct_results() {
		AssertStreamTail(
			"$projections-other_1-result", "0@$projections-other_1-user-Greg-result",
			"0@$projections-other_1-user-Ronan-result", "0@$projections-other_1-user-James-result",
			"0@$projections-other_1-user-Rob-result", "1@$projections-other_1-user-Ronan-result");
		AssertStreamTailWithLinks(
			"$projections-other_1-result", @"Result:{""count"":1}", @"Result:{""count"":1}",
			@"Result:{""count"":1}",
			@"Result:{""count"":1}", @"Result:{""count"":2}");
	}
}
