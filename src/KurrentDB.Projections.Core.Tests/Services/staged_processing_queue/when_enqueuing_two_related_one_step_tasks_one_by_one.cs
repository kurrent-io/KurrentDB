// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_two_related_one_step_tasks_one_by_one {
	private StagedProcessingQueue _q;
	private TestTask _t1;
	private TestTask _t2;

	[SetUp]
	public void when() {
		_q = new([true, true]);
		_t1 = new(1, 1, 1);
		_t2 = new(1, 1, 1);
		_q.Enqueue(_t1);
		_q.Process();
		_q.Enqueue(_t2);
		_q.Process();
	}

	[Test]
	public void two_process_execute_only_the_first_task() {
		Assert.That(_t1.StartedOn(0));
		Assert.That(_t2.StartedOn(0));
	}
}
