// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// A single tokenized text ready to feed a transformer encoder: the ids plus the masks the ONNX model
/// expects. Produced by a generator's tokenizer and consumed by <see cref="InferenceSessionExtensions"/>.
/// </summary>
/// <param name="InputIds">The token ids, including any model-specific special tokens (e.g. BERT's [CLS] … [SEP]).</param>
/// <param name="AttentionMask">1 for real tokens, 0 for padding. Same length as <paramref name="InputIds"/>.</param>
/// <param name="TokenTypeIds">
/// Optional segment ids. <see langword="null"/> when the tokenizer produces none; inference then feeds
/// zeros only if the ONNX model declares a <c>token_type_ids</c> input.
/// </param>
internal readonly record struct EncodedInput(long[] InputIds, long[] AttentionMask, long[]? TokenTypeIds);
