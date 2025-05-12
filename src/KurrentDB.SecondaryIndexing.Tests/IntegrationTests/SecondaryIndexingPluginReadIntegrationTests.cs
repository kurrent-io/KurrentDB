// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Tests;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Tests.Indices;
using Assert = Xunit.Assert;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ClientResolvedEvent = EventStore.ClientAPI.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public class when_reading_events_logV2 : when_reading<LogFormat.V2, string>;
public class when_reading_events_logV3 : when_reading<LogFormat.V3, uint>;

public abstract class when_reading<TLogFormat, TStreamId>
	: SecondaryIndexingPluginSpecification<TLogFormat, TStreamId> {
	private const string IndexStreamName = "$idx-dummy";
	private List<ResolvedEvent> _expectedEvents = [];
	private ClientResolvedEvent[] _readEventsFromIndex = null!;

	protected override ISecondaryIndex Given() {
		_expectedEvents = Enumerable.Range(0, 10)
			.Select(i => ToResolvedEvent(IndexStreamName, "test", $"{i}", i))
			.ToList();

		return new FakeSecondaryIndex(IndexStreamName, _expectedEvents);
	}

	protected override async Task When() {
		_readEventsFromIndex = await ReadStream(IndexStreamName);
	}

	[Fact]
	public void should_read_events() {
		Assert.Equal(_expectedEvents.Count, _readEventsFromIndex.Length);
		Assert.All(_readEventsFromIndex, e => Assert.Equal("test", e.Event.EventType));
	}
}
