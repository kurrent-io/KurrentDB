// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.transaction_file_position_tagger;

[TestFixture]
public class transaction_file_position_tagger {
	private ReaderSubscriptionMessage.CommittedEventDistributed _zeroEvent;
	private ReaderSubscriptionMessage.CommittedEventDistributed _firstEvent;
	private ReaderSubscriptionMessage.CommittedEventDistributed _secondEvent;

	[SetUp]
	public void setup() {
		_zeroEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(10, 0), "stream", 0, false, Guid.NewGuid(), "StreamCreated", false,
			new byte[0], new byte[0]);
		_firstEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(30, 20), "stream", 1, false, Guid.NewGuid(), "Data", true,
			Helper.UTF8NoBom.GetBytes("{}"), new byte[0]);
		_secondEvent = ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
			Guid.NewGuid(), new TFPos(50, 40), "stream", 2, false, Guid.NewGuid(), "Data", true,
			Helper.UTF8NoBom.GetBytes("{}"), new byte[0]);
	}

	[Test]
	public void can_be_created() {
		new TransactionFilePositionTagger(0);
	}

	[Test]
	public void is_message_after_checkpoint_tag_after_case() {
		var t = new TransactionFilePositionTagger(0);
		var result = t.IsMessageAfterCheckpointTag(CheckpointTag.FromPosition(0, 20, 10), _firstEvent);
		Assert.IsTrue(result);
	}

	[Test]
	public void is_message_after_checkpoint_tag_before_case() {
		var t = new TransactionFilePositionTagger(0);
		var result = t.IsMessageAfterCheckpointTag(CheckpointTag.FromPosition(0, 50, 40), _firstEvent);
		Assert.IsFalse(result);
	}

	[Test]
	public void is_message_after_checkpoint_tag_equal_case() {
		var t = new TransactionFilePositionTagger(0);
		var result = t.IsMessageAfterCheckpointTag(CheckpointTag.FromPosition(0, 30, 20), _firstEvent);
		Assert.IsFalse(result);
	}

	[Test]
	public void position_checkpoint_tag_is_compatible() {
		var t = new TransactionFilePositionTagger(0);
		Assert.IsTrue(t.IsCompatible(CheckpointTag.FromPosition(0, 1000, 500)));
	}

	[Test]
	public void stream_checkpoint_tag_is_incompatible() {
		var t = new TransactionFilePositionTagger(0);
		Assert.IsFalse(t.IsCompatible(CheckpointTag.FromStreamPosition(0, "stream2", 100)));
	}

	[Test]
	public void adjust_compatible_tag_returns_the_same_tag() {
		var t = new TransactionFilePositionTagger(0);
		var tag = CheckpointTag.FromPosition(0, 100, 50);
		Assert.AreSame(tag, t.AdjustTag(tag));
	}

	[Test]
	public void can_adjust_tf_position_tag() {
		var t = new TransactionFilePositionTagger(0);
		var tag = CheckpointTag.FromPosition(0, 100, 50);
		var original = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(100, 50),
			new Dictionary<string, long> {{"type1", 1}, {"type2", 2}});
		Assert.AreEqual(tag, t.AdjustTag(original));
	}

	[Test]
	public void zero_position_tag_is_before_first_event_possible() {
		var t = new TransactionFilePositionTagger(0);
		var zero = t.MakeZeroCheckpointTag();

		var zeroFromEvent = t.MakeCheckpointTag(zero, _zeroEvent);

		Assert.IsTrue(zeroFromEvent > zero);
	}

	[Test]
	public void produced_checkpoint_tags_are_correctly_ordered() {
		var t = new TransactionFilePositionTagger(0);
		var zero = t.MakeZeroCheckpointTag();

		var zeroEvent = t.MakeCheckpointTag(zero, _zeroEvent);
		var zeroEvent2 = t.MakeCheckpointTag(zeroEvent, _zeroEvent);
		var first = t.MakeCheckpointTag(zeroEvent2, _firstEvent);
		var second = t.MakeCheckpointTag(first, _secondEvent);
		var second2 = t.MakeCheckpointTag(zero, _secondEvent);

		Assert.IsTrue(zeroEvent > zero);
		Assert.IsTrue(first > zero);
		Assert.IsTrue(second > first);

		Assert.AreEqual(zeroEvent2, zeroEvent);
		Assert.AreEqual(second, second2);
	}
}
