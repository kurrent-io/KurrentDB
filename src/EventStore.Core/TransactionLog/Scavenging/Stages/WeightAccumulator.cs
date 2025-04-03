// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.Stages;

public class WeightAccumulator {
	const float DiscardWeight = 2.0f;
	const float MaybeDiscardWeight = 1.0f;

	private readonly IIncreaseChunkWeights _state;
	private readonly Dictionary<int, float> _weights;

	public WeightAccumulator(IIncreaseChunkWeights state) {
		_state = state;
		_weights = new Dictionary<int, float>();
	}

	public void OnDiscard(int logicalChunkNumber) => IncreaseChunkWeight(
		logicalChunkNumber,
		DiscardWeight);

	public void OnMaybeDiscard(int logicalChunkNumber) => IncreaseChunkWeight(
		logicalChunkNumber,
		MaybeDiscardWeight);

	private void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight) {
		if (_weights.TryGetValue(logicalChunkNumber, out var current)) {
			_weights[logicalChunkNumber] = extraWeight + current;
		} else {
			_weights[logicalChunkNumber] = extraWeight;
		}
	}

	public void Flush() {
		foreach (var kvp in _weights) {
			var logicalChunkNumber = kvp.Key;
			var extraWeight = kvp.Value;
			_state.IncreaseChunkWeight(logicalChunkNumber, extraWeight);
		}
		_weights.Clear();
	}
}
