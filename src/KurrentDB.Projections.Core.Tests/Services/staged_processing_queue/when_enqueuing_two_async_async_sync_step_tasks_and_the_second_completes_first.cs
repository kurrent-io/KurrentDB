// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_two_async_async_sync_step_tasks_and_the_second_completes_first {
	private StagedProcessingQueue _q;
	private TestTask _t1;
	private TestTask _t2;

	[SetUp]
	public void when() {
		_q = new([false, false, true]);
		_t1 = new(1, 3);
		_t2 = new(2, 3, 0);
		_q.Enqueue(_t1);
		_q.Enqueue(_t2);
		_q.Process(max: 3);
		_q.Process(max: 3);
		_q.Process(max: 3);
	}

	[Test]
	public void start_processing_second_task_on_stage_one() {
		Assert.That(_t1.StartedOn(0));
		Assert.That(_t2.StartedOn(1));
	}

	[Test]
	public void first_task_completed_unblocks_both_tasks() {
		_t1.Complete();
		_q.Process();
		_q.Process();

		Assert.That(_t1.StartedOn(1));
		Assert.That(_t2.StartedOn(1));
	}
}
