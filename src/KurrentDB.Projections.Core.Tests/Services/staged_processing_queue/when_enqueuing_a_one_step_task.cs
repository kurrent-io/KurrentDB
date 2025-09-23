// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_a_one_step_task {
	private StagedProcessingQueue _q;
	private TestTask _t1;

	[SetUp]
	public void when() {
		_q = new([true]);
		_t1 = new(1, 1);
		_q.Enqueue(_t1);
	}

	[Test]
	public void process_executes_the_task_at_stage_zero() {
		_q.Process();

		Assert.That(_t1.StartedOn(0));
	}

	[Test]
	public void process_returns_true() {
		var processed = _q.Process();

		Assert.IsTrue(processed);
	}
}
