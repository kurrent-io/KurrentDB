// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.multistream_position_tagger;

[TestFixture]
public class multistream_position_tagger {
	private ReaderSubscriptionMessage.CommittedEventDistributed _zeroEvent;
	private ReaderSubscriptionMessage.CommittedEventDistributed _firstEvent;
	private ReaderSubscriptionMessage.CommittedEventDistributed _secondEvent;
	private ReaderSubscriptionMessage.CommittedEventDistributed _thirdEvent;

	[SetUp]
	public void setup() {
		_zeroEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(10, 0), "stream1", 0, false, Guid.NewGuid(), "StreamCreated", false,
			new byte[0], new byte[0]);
		_firstEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(30, 20), "stream1", 1, false, Guid.NewGuid(), "Data", true,
			Helper.UTF8NoBom.GetBytes("{}"), new byte[0]);
		_secondEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(50, 40), "stream2", 0, false, Guid.NewGuid(), "StreamCreated", false,
			new byte[0], new byte[0]);
		_thirdEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(70, 60), "stream2", 1, false, Guid.NewGuid(), "Data", true,
			Helper.UTF8NoBom.GetBytes("{}"), new byte[0]);
	}

	[Test]
	public void can_be_created() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		new PositionTracker(t);
	}

	[Test]
	public void is_message_after_checkpoint_tag_after_case() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var result =
			t.IsMessageAfterCheckpointTag(
				CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream1", 0}, {"stream2", 0}}),
				_firstEvent);
		Assert.IsTrue(result);
	}

	[Test]
	public void is_message_after_checkpoint_tag_before_case() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var result =
			t.IsMessageAfterCheckpointTag(
				CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream1", 2}, {"stream2", 2}}),
				_firstEvent);
		Assert.IsFalse(result);
	}

	[Test]
	public void is_message_after_checkpoint_tag_equal_case() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var result =
			t.IsMessageAfterCheckpointTag(
				CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream1", 1}, {"stream2", 1}}),
				_firstEvent);
		Assert.IsFalse(result);
	}

	[Test]
	public void is_message_after_checkpoint_tag_incompatible_streams_case() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream-other", "stream2"});
		var result =
			t.IsMessageAfterCheckpointTag(
				CheckpointTag.FromStreamPositions(0,
					new Dictionary<string, long> {{"stream-other", 0}, {"stream2", 0}}),
				_firstEvent);
		Assert.IsFalse(result);
	}


	[Test]
	public void null_streams_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => { new MultiStreamPositionTagger(0, null); });
	}

	[Test]
	public void empty_streams_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => { new MultiStreamPositionTagger(0, new string[] { }); });
	}

	[Test]
	public void position_checkpoint_tag_is_incompatible() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		Assert.IsFalse(t.IsCompatible(CheckpointTag.FromPosition(0, 1000, 500)));
	}

	[Test]
	public void another_streams_checkpoint_tag_is_incompatible() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		Assert.IsFalse(
			t.IsCompatible(
				CheckpointTag.FromStreamPositions(0,
					new Dictionary<string, long> {{"stream2", 100}, {"stream3", 150}})));
	}

	[Test]
	public void the_same_stream_checkpoint_tag_is_compatible() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		Assert.IsTrue(
			t.IsCompatible(
				CheckpointTag.FromStreamPositions(0,
					new Dictionary<string, long> {{"stream1", 100}, {"stream2", 150}})));
	}

	[Test]
	public void adjust_compatible_tag_returns_the_same_tag() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var tag = CheckpointTag.FromStreamPositions(0,
			new Dictionary<string, long> {{"stream1", 1}, {"stream2", 2}});
		Assert.AreEqual(tag, t.AdjustTag(tag));
	}

	[Test]
	public void can_adjust_stream_position_tag() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var tag = CheckpointTag.FromStreamPositions(0,
			new Dictionary<string, long> {{"stream1", 1}, {"stream2", -1}});
		var original = CheckpointTag.FromStreamPosition(0, "stream1", 1);
		Assert.AreEqual(tag, t.AdjustTag(original));
	}

	[Test]
	public void zero_position_tag_is_before_first_event_possible() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var zero = t.MakeZeroCheckpointTag();

		var zeroFromEvent = t.MakeCheckpointTag(zero, _zeroEvent);

		Assert.IsTrue(zeroFromEvent > zero);
	}

	[Test]
	public void produced_checkpoint_tags_are_correctly_ordered() {
		var t = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var zero = t.MakeZeroCheckpointTag();

		var zeroEvent = t.MakeCheckpointTag(zero, _zeroEvent);
		var zeroEvent2 = t.MakeCheckpointTag(zeroEvent, _zeroEvent);
		var first = t.MakeCheckpointTag(zeroEvent2, _firstEvent);
		var second = t.MakeCheckpointTag(first, _secondEvent);
		var second2 = t.MakeCheckpointTag(zeroEvent, _secondEvent);
		var third = t.MakeCheckpointTag(second, _thirdEvent);

		Assert.IsTrue(zeroEvent > zero);
		Assert.IsTrue(first > zero);
		Assert.IsTrue(second > first);

		Assert.AreEqual(zeroEvent2, zeroEvent);
		Assert.AreNotEqual(second, second2);
		Assert.IsTrue(second2 > zeroEvent);
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(second2 > first));

		Assert.IsTrue(third > second);
		Assert.IsTrue(third > first);
		Assert.IsTrue(third > zeroEvent);
		Assert.IsTrue(third > zero);
	}
}
