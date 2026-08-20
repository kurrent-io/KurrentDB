// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// The extraction seam of the resolution pipeline. Implementations compose: the processor sees
/// one extractor whether that is a single strategy or a pipeline over several.
/// </summary>
public interface IEntityExtractor {
    /// <summary>
    /// The entities named in the content, at most one per normalized surface form. That
    /// contract is what makes a collision in the pipeline's merge two opinions about one
    /// occurrence rather than two entities.
    /// </summary>
    ValueTask<IReadOnlyList<ExtractedEntity>> ExtractAsync(string content, CancellationToken ct = default);
}
