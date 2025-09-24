// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_a_two_step_task_and_both_steps_complete_immediately {
	private StagedProcessingQueue _q;
	private TestTask _t1;

	[SetUp]
	public void when() {
		_q = new([true, true]);
		_t1 = new(1, 2, 1);
		_q.Enqueue(_t1);
	}

	[Test]
	public void queue_becomes_empty() {
		_q.Process(max: 2);

		Assert.That(_q.Count == 0);
	}

	[Test]
	public void process_returns_true() {
		var processed = _q.Process(max: 2);

		Assert.IsTrue(processed);
	}
}
