// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services;

public class TestTask : StagedTask {
	private readonly int _steps;
	private readonly int _completeImmediatelyUpToStage;
	private readonly object[] _stageCorrelations;
	private Action<int, object> _readyForStage;
	private int _startedOnStage;

	public TestTask(
		object initialCorrelationId, int steps, int completeImmediatelyUpToStage = -1,
		object[] stageCorrelations = null)
		: base(initialCorrelationId) {
		_steps = steps;
		_completeImmediatelyUpToStage = completeImmediatelyUpToStage;
		_stageCorrelations = stageCorrelations;
		_startedOnStage = -1;
	}

	public bool StartedOn(int onStage) {
		return _startedOnStage >= onStage;
	}

	public override void Process(int onStage, Action<int, object> readyForStage) {
		_readyForStage = readyForStage;
		_startedOnStage = onStage;
		if (_startedOnStage <= _completeImmediatelyUpToStage)
			Complete();
	}

	public void Complete() {
		var correlationId = _stageCorrelations != null ? _stageCorrelations[_startedOnStage] : InitialCorrelationId;
		_readyForStage(_startedOnStage == _steps - 1 ? -1 : _startedOnStage + 1, correlationId);
	}
}
