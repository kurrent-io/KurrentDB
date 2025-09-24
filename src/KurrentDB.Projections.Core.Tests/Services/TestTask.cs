// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing;

namespace KurrentDB.Projections.Core.Tests.Services;

public class TestTask(
	object initialCorrelationId,
	int steps,
	int completeImmediatelyUpToStage = -1,
	object[] stageCorrelations = null)
	: StagedTask(initialCorrelationId) {
	private Action<int, object> _readyForStage;
	private int _startedOnStage = -1;

	public bool StartedOn(int onStage) {
		return _startedOnStage >= onStage;
	}

	public override void Process(int onStage, Action<int, object> readyForStage) {
		_readyForStage = readyForStage;
		_startedOnStage = onStage;
		if (_startedOnStage <= completeImmediatelyUpToStage)
			Complete();
	}

	public void Complete() {
		var correlationId = stageCorrelations != null ? stageCorrelations[_startedOnStage] : InitialCorrelationId;
		_readyForStage(_startedOnStage == steps - 1 ? -1 : _startedOnStage + 1, correlationId);
	}
}
