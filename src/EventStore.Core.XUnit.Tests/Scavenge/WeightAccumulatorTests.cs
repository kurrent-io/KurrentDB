// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.Core.TransactionLog.Scavenging.Stages;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class WeightAccumulatorTests {
	private readonly WeightAccumulator _sut;
	private readonly MockIncreaseChunkWeights _state;

	class MockIncreaseChunkWeights : IIncreaseChunkWeights {
		private readonly Dictionary<int, float> _weights = new Dictionary<int, float>();

		public void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight) {
			if (!_weights.TryGetValue(logicalChunkNumber, out var w))
				w = 0;
			_weights[logicalChunkNumber] = w + extraWeight;
		}

		public float SumChunkWeights(int from, int to) =>
			_weights
				.Where(x => from <= x.Key && x.Key <= to)
				.Select(x => x.Value)
				.Sum();
	}

	public WeightAccumulatorTests() {
		_state = new MockIncreaseChunkWeights();
		_sut = new WeightAccumulator(_state);
	}

	[Fact]
	public void sanity() {
		_sut.OnDiscard(0);
		_sut.OnDiscard(1);
		_sut.OnMaybeDiscard(0);

		Assert.Equal(0, _state.SumChunkWeights(0, 0));
		Assert.Equal(0, _state.SumChunkWeights(1, 1));

		_sut.Flush();
		Assert.Equal(3, _state.SumChunkWeights(0, 0));
		Assert.Equal(2, _state.SumChunkWeights(1, 1));

		_sut.OnMaybeDiscard(1);
		_sut.Flush();
		Assert.Equal(3, _state.SumChunkWeights(0, 0));
		Assert.Equal(3, _state.SumChunkWeights(1, 1));
	}
}
