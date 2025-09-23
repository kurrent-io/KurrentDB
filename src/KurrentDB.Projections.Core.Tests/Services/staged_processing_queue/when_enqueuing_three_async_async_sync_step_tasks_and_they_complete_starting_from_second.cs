// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_three_async_async_sync_step_tasks_and_they_complete_starting_from_second {
	private StagedProcessingQueue _q;
	private TestTask _t1;
	private TestTask _t2;
	private TestTask _t3;

	[SetUp]
	public void when() {
		_q = new([false, false, true]);
		_t1 = new(1, 3);
		_t2 = new(2, 3, 0);
		_t3 = new(3, 3, 0);
		_q.Enqueue(_t1);
		_q.Enqueue(_t2);
		_q.Enqueue(_t3);
		_q.Process(max: 3);
		_q.Process(max: 3);
		_q.Process(max: 3);
	}

	[Test]
	public void start_processing_second_and_third_tasks_on_stage_one() {
		Assert.That(_t1.StartedOn(0));
		Assert.That(_t2.StartedOn(1));
		Assert.That(_t3.StartedOn(1));
	}

	[Test]
	public void first_task_keeps_other_blocked_at_stage_two() {
		_t1.Complete();
		_q.Process(max: 2);
		_q.Process(max: 2);
		_t2.Complete();
		_t3.Complete();
		_q.Process();
		Assert.That(!_t2.StartedOn(2));
		Assert.That(!_t3.StartedOn(2));
	}

	[Test]
	public void first_task_completed_at_stage_one_unblock_all() {
		_t1.Complete();
		_q.Process();
		_q.Process();
		_t2.Complete();
		_t3.Complete();
		_q.Process();
		_t1.Complete();
		_q.Process();
		_q.Process();
		_q.Process();

		Assert.That(_t2.StartedOn(2));
		Assert.That(_t3.StartedOn(2));
	}
}
