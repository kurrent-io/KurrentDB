// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using KurrentDB.Core.Tests;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Tests.Indices;
using Assert = Xunit.Assert;
using ClientResolvedEvent = EventStore.ClientAPI.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public class when_appending_and_disabled_logV2 : when_appending_and_disabled<LogFormat.V2, string>;
public class when_appending_and_disabled_logV3 : when_appending_and_disabled<LogFormat.V3, uint>;

public abstract class when_appending_and_disabled<TLogFormat, TStreamId>
	: SecondaryIndexingPluginSpecification<TLogFormat, TStreamId> {
	private const string IndexStreamName = "$idx-dummy";
	private readonly string _streamName = $"test-{Guid.NewGuid()}";

	private readonly string[] _expectedEventData = ["""{"test"="123"}""", """{"test"="321"}"""];
	private ClientResolvedEvent[] _readEventsFromIndex = null!;

	protected override ISecondaryIndex Given() =>
		new FakeSecondaryIndex(IndexStreamName);

	protected override async Task When() {
		var result = await AppendToStream(_streamName, _expectedEventData);
		_readEventsFromIndex = await ReadUntil(IndexStreamName, result.LogPosition);
	}

	[Fact]
	public void should_read_events() {
		Assert.Empty(_readEventsFromIndex);
	}
}


public class when_appending_and_enabled_logV2 : when_appending_and_enabled<LogFormat.V2, string>;
public class when_appending_and_enabled_logV3 : when_appending_and_enabled<LogFormat.V3, uint>;

public abstract class when_appending_and_enabled<TLogFormat, TStreamId>
	: SecondaryIndexingPluginSpecification<TLogFormat, TStreamId> {

	private const string IndexStreamName = "$idx-dummy";
	private readonly string _streamName = $"test-{Guid.NewGuid()}";

	private readonly EventData[] _expectedEventData = [
		ToEventData("""{"test"="123"}"""),
		ToEventData("""{"test"="321"}""")
	];
	private ClientResolvedEvent[] _readEventsFromIndex = null!;

	protected override ISecondaryIndex Given() {
		IsPluginEnabled = true;

		return new FakeSecondaryIndex(IndexStreamName);
	}

	protected override async Task When() {
		var result = await AppendToStream(_streamName, _expectedEventData);

		_readEventsFromIndex = await ReadUntil(IndexStreamName, result.LogPosition);
	}

	[Fact]
	public void should_read_events() {
		Assert.Equal(_expectedEventData.Length, _readEventsFromIndex.Length);
		//Assert.All(_readEventsSlice.Events, e => Assert.Equal("test", e.Event.EventType));
	}
}
