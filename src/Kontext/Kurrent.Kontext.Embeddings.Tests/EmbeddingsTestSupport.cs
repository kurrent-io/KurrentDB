// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace Kurrent.Kontext.Embeddings.Tests;

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

	// The ONNX assets are large and not committed; KurrentDB.Kontext.Models downloads them on build
	// into its own (gitignored) per-model directories. [CallerFilePath] resolves this test project's
	// directory at compile time so the path holds wherever the repo lives.
	static string TestsDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
	static string ModelsDir => Path.Combine(TestsDir(), "..", "..", "KurrentDB.Kontext.Models");

	// DownloadE5IfMissing.
	static string E5Dir => Path.Combine(ModelsDir, "e5-small");
	public static string E5ModelPath => Path.Combine(E5Dir, "model.onnx");
	public static string E5SentencePiecePath => Path.Combine(E5Dir, "sentencepiece.bpe.model");

	// The GLiNER assets follow the OnnxModelRegistry layout (<key>/onnx/<model>, <key>/<asset>).
	// DownloadGlinerIfMissing.
	static string GlinerDir => Path.Combine(ModelsDir, "gliner-small-v2.1");
	public static string GlinerModelPath => Path.Combine(GlinerDir, "onnx", "model_quantized.onnx");
	public static string GlinerSentencePiecePath => Path.Combine(GlinerDir, "spm.model");
}
