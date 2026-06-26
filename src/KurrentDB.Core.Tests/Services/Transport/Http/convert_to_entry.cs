// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Transport.Http.Atom;
using NUnit.Framework;
using Convert = KurrentDB.Core.Services.Transport.Http.Convert;

namespace KurrentDB.Core.Tests.Services.Transport.Http;

[TestFixture]
public class convert_to_entry {
	private static readonly Uri RequestedUrl = new("http://localhost:2113/streams/test");

	private static EventRecord CreateLinkEvent(long eventNumber, string targetStream, long targetEventNumber) {
		var linkData = Encoding.UTF8.GetBytes($"{targetEventNumber}@{targetStream}");
		return new EventRecord(
			eventNumber, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			"$persistentsubscription-test-group-parked", 0,
			DateTime.UtcNow, PrepareFlags.None, "$>",
			linkData, Encoding.UTF8.GetBytes("{\"some\":\"metadata\"}"));
	}

	private static EventRecord CreateEvent(long eventNumber, string stream) {
		return new EventRecord(
			eventNumber, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			stream, 0,
			DateTime.UtcNow, PrepareFlags.IsJson, "TestEvent",
			Encoding.UTF8.GetBytes("{\"key\":\"value\"}"), Encoding.UTF8.GetBytes("{}"));
	}

	[Test]
	public void unresolved_link_produces_rich_entry_with_position_fields() {
		var link = CreateLinkEvent(5, "source-stream", 42);
		var resolved = ResolvedEvent.ForFailedResolvedLink(link, ReadEventResult.StreamDeleted);

		var entry = Convert.ToEntry(resolved, RequestedUrl, EmbedLevel.Body);

		Assert.That(entry, Is.InstanceOf<RichEntryElement>());
		var rich = (RichEntryElement)entry;
		Assert.That(rich.PositionEventNumber, Is.EqualTo(5));
		Assert.That(rich.PositionStreamId, Is.EqualTo("$persistentsubscription-test-group-parked"));
	}

	[Test]
	public void unresolved_link_does_not_populate_resolved_event_fields() {
		var link = CreateLinkEvent(5, "source-stream", 42);
		var resolved = ResolvedEvent.ForFailedResolvedLink(link, ReadEventResult.StreamDeleted);

		var entry = Convert.ToEntry(resolved, RequestedUrl, EmbedLevel.Body);

		var rich = (RichEntryElement)entry;
		Assert.That(rich.EventId, Is.EqualTo(Guid.Empty));
		Assert.That(rich.EventType, Is.Null);
		Assert.That(rich.Data, Is.Null);
	}

	[Test]
	public void unresolved_link_populates_link_metadata() {
		var link = CreateLinkEvent(5, "source-stream", 42);
		var resolved = ResolvedEvent.ForFailedResolvedLink(link, ReadEventResult.StreamDeleted);

		var entry = Convert.ToEntry(resolved, RequestedUrl, EmbedLevel.Body);

		var rich = (RichEntryElement)entry;
		Assert.That(rich.LinkMetaData, Is.EqualTo("{\"some\":\"metadata\"}"));
		Assert.That(rich.IsLinkMetaData, Is.True);
	}

	[Test]
	public void resolved_link_produces_rich_entry_with_all_fields() {
		var targetEvent = CreateEvent(42, "source-stream");
		var link = CreateLinkEvent(5, "source-stream", 42);
		var resolved = ResolvedEvent.ForResolvedLink(targetEvent, link);

		var entry = Convert.ToEntry(resolved, RequestedUrl, EmbedLevel.Body);

		Assert.That(entry, Is.InstanceOf<RichEntryElement>());
		var rich = (RichEntryElement)entry;
		Assert.That(rich.PositionEventNumber, Is.EqualTo(5));
		Assert.That(rich.PositionStreamId, Is.EqualTo("$persistentsubscription-test-group-parked"));
		Assert.That(rich.EventId, Is.EqualTo(targetEvent.EventId));
		Assert.That(rich.EventType, Is.EqualTo("TestEvent"));
		Assert.That(rich.EventNumber, Is.EqualTo(42));
		Assert.That(rich.StreamId, Is.EqualTo("source-stream"));
	}

	[Test]
	public void unresolved_link_with_content_embed_does_not_produce_rich_entry() {
		var link = CreateLinkEvent(5, "source-stream", 42);
		var resolved = ResolvedEvent.ForFailedResolvedLink(link, ReadEventResult.StreamDeleted);

		var entry = Convert.ToEntry(resolved, RequestedUrl, EmbedLevel.Content);

		Assert.That(entry, Is.Not.InstanceOf<RichEntryElement>());
	}
}
