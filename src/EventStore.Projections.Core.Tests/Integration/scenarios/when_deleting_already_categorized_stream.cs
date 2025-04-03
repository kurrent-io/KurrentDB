// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Integration.scenarios;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleting_already_categorized_stream<TLogFormat, TStreamId> : specification_with_a_v8_query_posted<TLogFormat, TStreamId> {
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
