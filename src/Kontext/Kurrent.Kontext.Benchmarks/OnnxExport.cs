// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Benchmarks;

/// <summary>
/// The export precisions a model is published at. One entry per thing that changes the numbers the
/// model produces — not one per file.
/// </summary>
/// <remarks>
/// Deliberately absent: the O1-O3 graph-optimized builds, which are the same size as fp32 because
/// they are operator fusion and emit identical vectors, and the avx2/avx512/vnni int8 builds, which
/// are one quantization compiled for instruction sets this machine does not have. Both would spend a
/// corpus rebuild to measure nothing.
/// </remarks>
public enum OnnxExport {
	/// <summary>No quantization.</summary>
	Fp32,

	/// <summary>Half precision. ONNX Runtime's CPU provider casts most of this back to fp32.</summary>
	Fp16,

	/// <summary>
	/// The Xenova recipe: int8 weights, attention left in float32. ONNX Runtime's dynamic
	/// quantization skips activation×activation matmuls by default, which is what this preserves.
	/// </summary>
	Int8Partial,

	/// <summary>Unsigned int8, otherwise the same recipe as <see cref="Int8Partial"/>.</summary>
	Uint8,

	/// <summary>The official recipe: everything int8, attention included.</summary>
	Int8Full,

	/// <summary>
	/// 4-bit encoder. Larger on disk than int8 for these models, because the vocab table stays fp32
	/// and it dominates the parameter count.
	/// </summary>
	Q4,
}
