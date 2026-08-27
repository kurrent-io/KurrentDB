// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.GlinerOnnx;

/// <summary>
/// One recognized entity span: the surface form, the label it matched, the sigmoid score, and its
/// character range (<see cref="Start"/> inclusive, <see cref="End"/> exclusive) in the source text.
/// </summary>
public readonly record struct RecognizedSpan(string Text, string Label, double Score, int Start, int End);
