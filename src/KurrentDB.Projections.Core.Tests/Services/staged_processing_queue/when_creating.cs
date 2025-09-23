// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_creating {
	private StagedProcessingQueue _q;

	[SetUp]
	public void when() {
		_q = new StagedProcessingQueue([true]);
	}

	[Test]
	public void it_can_be_created() {
		Assert.IsNotNull(_q);
	}

	[Test]
	public void task_can_be_enqueued() {
		_q.Enqueue(new TestTask(1, 1));
	}

	[Test]
	public void process_does_not_proces_anything() {
		var processed = _q.Process();
		Assert.IsFalse(processed);
	}
}
