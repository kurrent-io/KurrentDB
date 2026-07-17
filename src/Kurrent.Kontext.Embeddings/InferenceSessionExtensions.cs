// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// Shared, tokenizer-agnostic ONNX inference for the embedding generators. Given a tokenized
/// <see cref="EncodedInput"/> it builds the model inputs (feeding <c>token_type_ids</c> only when the model
/// declares that input), runs the session, pools over the attention mask, and optionally L2-normalizes.
/// Each generator owns its session + tokenizer and calls <c>session.Embed(…)</c> to turn one text into a
/// vector — the math is identical across generators; only the tokenizer differs.
/// </summary>
internal static class InferenceSessionExtensions {
	extension(InferenceSession session) {
		/// <summary>
		/// Runs one tokenized text through the model and pools it into a single embedding. Used by every
		/// generator's <c>GenerateAsync</c> and by their constructor warm-up probe (which reads the true
		/// output dimension from the returned vector).
		/// </summary>
		internal float[] Embed(EncodedInput input, bool feedTokenTypeIds, EmbeddingPoolingMode poolingMode, bool normalize) {
			var seqLen = input.InputIds.Length;

			var onnxInputs = new List<NamedOnnxValue>(3) {
				NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(input.InputIds, [1, seqLen])),
				NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(input.AttentionMask, [1, seqLen])),
			};
			
			if (feedTokenTypeIds)
				onnxInputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
					new DenseTensor<long>(input.TokenTypeIds ?? new long[seqLen], [1, seqLen])));

			using var results = session.Run(onnxInputs);
			var output = results[0].AsTensor<float>(); // last_hidden_state: [1, seqLen, dim]

			var embedding = poolingMode switch {
				EmbeddingPoolingMode.Mean => MeanPool(output, input.AttentionMask, seqLen),
				EmbeddingPoolingMode.Cls  => ClsPool(output),
				_ => throw new NotSupportedException($"Pooling mode '{poolingMode}' is not supported."),
			};

			if (normalize)
				L2Normalize(embedding);

			return embedding;
			
			static float[] MeanPool(Tensor<float> output, long[] attentionMask, int seqLen) {
				var dim = output.Dimensions[2];
				var embedding = new float[dim];

				float maskSum = 0;
				for (var i = 0; i < seqLen; i++) {
					if (attentionMask[i] == 0)
						continue;
					maskSum++;
					for (var j = 0; j < dim; j++)
						embedding[j] += output[0, i, j];
				}

				if (maskSum > 0)
					for (var j = 0; j < dim; j++)
						embedding[j] /= maskSum;

				return embedding;
			}

			// CLS pooling: use the first token's vector (the <s>/[CLS] representation).
			// Models such as bge-m3 are trained for this rather than mean pooling.
			static float[] ClsPool(Tensor<float> output) {
				var dim = output.Dimensions[2];
				var embedding = new float[dim];
				for (var j = 0; j < dim; j++)
					embedding[j] = output[0, 0, j];
				return embedding;
			}

			static void L2Normalize(float[] embedding) {
				var norm = MathF.Sqrt(embedding.Sum(x => x * x));
				if (norm <= 0) return;
				for (var j = 0; j < embedding.Length; j++)
					embedding[j] /= norm;
			}
		}
	}
}
