// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_prerecording_event_order<TLogFormat, TStreamId> : TestFixtureWithMultiStreamCheckpointManager<TLogFormat, TStreamId> {
	private ResolvedEvent _event1;
	private ResolvedEvent _event2;

	protected override void Given() {
		base.Given();
		_streams = new[] {"pa", "pb"};
		ExistingEvent("a", "test1", "{}", "{}");
		ExistingEvent("b", "test1", "{}", "{}");

		ExistingEvent("pa", "$>", "1@a", "{$o:\"oa\"}");
		ExistingEvent("pb", "$>", "1@b", "{$o:\"ob\"}");

		_event1 = new ResolvedEvent("pa", 1, "a", 1, true, new TFPos(200, 150), Guid.NewGuid(), "test1", true, "{}",
			"{}", "{$o:\"oa\"");
		_event2 = new ResolvedEvent("pb", 1, "b", 1, true, new TFPos(300, 250), Guid.NewGuid(), "test1", true, "{}",
			"{}", "{$o:\"ob\"");

		NoOtherStreams();
		AllWritesSucceed();
	}

	protected override void When() {
		base.When();
		Action noop = () => { };
		_manager.Initialize();
		_checkpointReader.BeginLoadState();
		var checkpointLoaded =
			_consumer.HandledMessages.OfType<CoreProjectionProcessingMessage.CheckpointLoaded>().First();
		_checkpointWriter.StartFrom(checkpointLoaded.CheckpointTag, checkpointLoaded.CheckpointEventNumber);
		_manager.BeginLoadPrerecordedEvents(checkpointLoaded.CheckpointTag);

		_manager.Start(CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"pa", -1}, {"pb", -1}}),
			null);
		_manager.RecordEventOrder(_event1,
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"pa", 1}, {"pb", -1}}),
			committed: noop);
		_manager.RecordEventOrder(_event2,
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"pa", 1}, {"pb", 1}}),
			committed: noop);
	}

	[Test]
	public void writes_correct_link_tos() {
		var writeEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.SelectMany(v => v.Events)
				.Where(v => v.EventType == SystemEventTypes.LinkTo)
				.ToArray();
		Assert.AreEqual(2, writeEvents.Length);
		Assert.AreEqual("1@pa", Helper.UTF8NoBom.GetString(writeEvents[0].Data));
		Assert.AreEqual("1@pb", Helper.UTF8NoBom.GetString(writeEvents[1].Data));
	}
}
