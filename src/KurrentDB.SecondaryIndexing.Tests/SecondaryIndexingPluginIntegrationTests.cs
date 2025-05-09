// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Tests;
using KurrentDB.SecondaryIndexing.Indices;
using NUnit.Framework;
using Assert = Xunit.Assert;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Tests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("SecondaryIndexing")]
public class when_appending_events<TLogFormat, TStreamId>
	: SecondaryIndexingPluginSpecification<TLogFormat, TStreamId> {
	private const string StreamName = "$idx-dummy";
	private IList<ResolvedEvent> _expectedEvents = [];
	private StreamEventsSlice? _readEventsSlice;

	public override ISecondaryIndex Given() {
		_expectedEvents = Enumerable.Range(0, 10)
			.Select(i => CreateResolvedEvent(StreamName, "test", $"{i}", i))
			.ToList();

		return new FakeSecondaryIndex(StreamName, _expectedEvents);
	}

	public override async Task When() {
		_readEventsSlice = await ReadStream(StreamName);
	}

	[Test]
	public void should_read_events() {
		Assert.NotNull(_readEventsSlice);
		Assert.Equal(_expectedEvents.Count, _readEventsSlice.Events.Length);
		Assert.All(_readEventsSlice.Events, e => Assert.Equal("test", e.Event.EventType));
	}
}
