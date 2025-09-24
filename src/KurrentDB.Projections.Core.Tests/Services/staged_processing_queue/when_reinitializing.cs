// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_reinitializing {
	private StagedProcessingQueue _q;
	private TestTask _t1;

	[SetUp]
	public void when() {
		_q = new([true, true]);
		_t1 = new(1, 2, 0);
		_q.Enqueue(_t1);
		_q.Initialize();
	}

	[Test]
	public void process_does_not_execute_a_task() {
		_q.Process();

		Assert.That(!_t1.StartedOn(0));
	}

	[Test]
	public void queue_length_iz_zero() {
		Assert.AreEqual(0, _q.Count);
	}

	[Test]
	public void task_can_be_enqueued() {
		_q.Enqueue(new TestTask(1, 1));
	}
}
