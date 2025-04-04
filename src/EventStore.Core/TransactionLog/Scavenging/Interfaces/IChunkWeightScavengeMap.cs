// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkWeightScavengeMap : IScavengeMap<int, float> {
	bool AllWeightsAreZero();
	void IncreaseWeight(int logicalChunkNumber, float extraWeight);
	float SumChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber);
	void ResetChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber);
}
