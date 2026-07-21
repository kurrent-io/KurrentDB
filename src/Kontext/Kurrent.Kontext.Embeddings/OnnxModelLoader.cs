// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.ML.OnnxRuntime;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// Shared helpers for turning ONNX model streams into an <see cref="InferenceSession"/>.
/// </summary>
internal static class OnnxModelLoader {
	internal static InferenceSession CreateSession(byte[] model) =>
		new(model, new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });

	internal static byte[] ReadAllBytes(Stream stream) {
		if (stream is MemoryStream ms)
			return ms.ToArray();
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	internal static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken) {
		using var buffer = new MemoryStream();
		await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
		return buffer.ToArray();
	}
}
