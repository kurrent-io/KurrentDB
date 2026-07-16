// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace KurrentDB.Kontext.Embeddings.Tests;

/// <summary>Shared helpers for the embedding-generator tests.</summary>
static class EmbeddingsTestSupport {
	/// <summary>Every model under test (all-MiniLM and multilingual-e5-small) is 384-dimensional.</summary>
	public const int Dimensions = 384;

	/// <summary>Cosine similarity between two equal-length vectors.</summary>
	public static double Cosine(float[] a, float[] b) {
		double dot = 0, na = 0, nb = 0;
		for (var i = 0; i < a.Length; i++) {
			dot += (double)a[i] * b[i];
			na += (double)a[i] * a[i];
			nb += (double)b[i] * b[i];
		}
		return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
	}

	/// <summary>L2 norm of a vector (≈ 1.0 for a normalized embedding).</summary>
	public static double L2Norm(float[] v) => Math.Sqrt(v.Sum(x => (double)x * x));

	// The e5 assets are large and not committed; as agreed, the C tests point at the copy the
	// Embeddings Playground already downloaded into its (gitignored) models/ cache. [CallerFilePath]
	// resolves this test project's directory at compile time so the path holds wherever the repo lives.
	static string TestsDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
	static string PlaygroundModelsDir => Path.Combine(TestsDir(), "..", "KurrentDB.Kontext.Embeddings.Playground", "models");
	public static string E5ModelPath => Path.Combine(PlaygroundModelsDir, "model.onnx");
	public static string E5SentencePiecePath => Path.Combine(PlaygroundModelsDir, "sentencepiece.bpe.model");
}
