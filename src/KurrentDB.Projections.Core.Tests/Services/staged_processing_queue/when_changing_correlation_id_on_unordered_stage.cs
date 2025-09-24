// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.staged_processing_queue;

[TestFixture]
public class when_changing_correlation_id_on_unordered_stage {
	private StagedProcessingQueue _q;
	private TestTask _t1;

	[SetUp]
	public void when() {
		_q = new([false]);
		_t1 = new(Guid.NewGuid(), 1, stageCorrelations: ["a"]);
		_q.Enqueue(_t1);
	}

	[Test]
	public void first_task_starts_on_second_stage_on_first_stage_completion() {
		_q.Process();
		Assert.Throws<InvalidOperationException>(() => { _t1.Complete(); });
	}
}
