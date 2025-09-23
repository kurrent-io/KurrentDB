// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_enqueuing_a_two_step_task_and_the_first_step_completes_immediately {
	private StagedProcessingQueue _q;
	private TestTask _t1;

	[SetUp]
	public void when() {
		_q = new([true, true]);
		_t1 = new(1, 2, 0);
		_q.Enqueue(_t1);
	}

	[Test]
	public void process_executes_the_task_up_to_stage_one() {
		_q.Process(max: 2);

		Assert.That(_t1.StartedOn(1));
	}
}
