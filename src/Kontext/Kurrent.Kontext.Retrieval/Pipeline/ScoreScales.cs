// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The scale a pool's running <see cref="ScoredMemory.Score"/> lives on. Carried as a type
/// parameter on <see cref="Pool{TScale}"/> so a chain states, link by link, what its scores mean
/// — and a cut calibrated for one scale cannot be composed onto another by accident. A
/// <see cref="RetrievalQuery.MinScore"/> threshold is only meaningful against the scale the
/// chain ends on; never carry a nonzero cutoff across chains that end on different scales.
/// </summary>
public interface IScoreScale;

/// <summary>Reciprocal-rank magnitudes (Σ weight/(k+rank), ~0.01–0.03 for k=60). Ordering is meaningful, absolute values are not.</summary>
public sealed class RrfScale : IScoreScale;

/// <summary>Scores normalized onto [0,1] — min-max, sigmoid-squashed, or rescaled against a theoretical maximum.</summary>
public sealed class UnitScale : IScoreScale;

/// <summary>Source- or model-native scores passed through unrescaled — raw BM25, engine blends, relevance-model judgments.</summary>
public sealed class NativeScale : IScoreScale;
