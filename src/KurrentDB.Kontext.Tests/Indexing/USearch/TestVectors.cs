// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

// Small deterministic, unit-normalized embeddings for segment/merge tests.
static class TestVectors {
	public const int Dims = 8;

	public static ReadOnlyMemory<float> Embedding(int seed) {
		var rng = new Random(seed);
		var v = new float[Dims];
		for (var i = 0; i < Dims; i++)
			v[i] = (float)(rng.NextDouble() * 2 - 1);

		var norm = MathF.Sqrt(v.Sum(x => x * x));
		if (norm > 0)
			for (var i = 0; i < Dims; i++)
				v[i] /= norm;
		return v;
	}
}
