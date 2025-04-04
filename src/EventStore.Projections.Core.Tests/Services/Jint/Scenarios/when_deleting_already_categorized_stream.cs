// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint.Scenarios;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(long))]
public class when_deleting_already_categorized_stream<TLogFormat, TStreamId> : specification_with_js_query_posted<TLogFormat, TStreamId> {
	protected override void GivenEvents() {
	}

	protected override IEnumerable<WhenStep> When() {
		foreach (var e in base.When())
			yield return e;
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
		var corrId = Guid.NewGuid();
		yield return
			new ClientMessage.DeleteStream(
				corrId, corrId, Envelope, false, "chat-2", ExpectedVersion.Any, true, null);
		yield return CreateNewProjectionMessage("test1", @"
fromCategory('chat').when({
    ChatMessage: function(s, e) {
        copyTo('out1', e);
    }
})
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

	[Test, Explicit]
	public void just() {
		DumpStream("$$chat-2");
		DumpStream("$ce-chat");
		DumpStream("out1");
	}
}
