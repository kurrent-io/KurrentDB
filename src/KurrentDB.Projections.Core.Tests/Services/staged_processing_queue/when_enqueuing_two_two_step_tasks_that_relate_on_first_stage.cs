// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_two_two_step_tasks_that_relate_on_first_stage {
	private StagedProcessingQueue _q;
	private TestTask _t1;
	private TestTask _t2;

	[SetUp]
	public void when() {
		_q = new([true, true]);
		_t1 = new(null, 2, stageCorrelations: ["a", "a"]);
		_t2 = new(null, 2, stageCorrelations: ["a", "a"]);
		_q.Enqueue(_t1);
		_q.Enqueue(_t2);
	}

	[Test]
	public void first_task_starts_on_second_stage_on_first_stage_completion() {
		_q.Process();
		_q.Process();

		_t1.Complete();

		_q.Process();

		Assert.That(_t1.StartedOn(1));
	}

	[Test]
	public void second_task_does_not_start_on_second_stage_on_first_stage_completion() {
		_q.Process();
		_q.Process();

		_t2.Complete();

		_q.Process();
		_q.Process();

		Assert.That(!_t2.StartedOn(1));
	}

	[Test]
	public void second_task_does_not_start_on_both_task_completion_on_the_first_stage() {
		_q.Process();
		_q.Process();

		_t1.Complete();
		_t2.Complete();

		_q.Process();
		_q.Process();

		Assert.That(!_t2.StartedOn(1));
	}

	[Test]
	public void second_task_starts_on_the_first_task_completion_on_the_first_stage() {
		_q.Process();
		_q.Process();

		_t1.Complete();
		_t2.Complete();

		_q.Process();
		_q.Process();

		_t1.Complete();

		_q.Process();

		Assert.That(_t2.StartedOn(1));
	}
}
