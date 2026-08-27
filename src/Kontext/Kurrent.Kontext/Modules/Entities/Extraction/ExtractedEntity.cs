// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Entities.Extraction;

/// <summary>
/// One surface form an extractor found, with its guess at the type. Not a mention yet, linking
/// it to an entity is resolution's job. Confidence is the extractor's certainty the text names
/// a real entity, the merge tiebreak, not the resolver's link confidence.
/// </summary>
public readonly record struct ExtractedEntity(string Text, string EntityType, double Confidence) {
    public bool IsClassified => EntityType != EntityTypes.Unknown;

    /// <summary>
    /// Whether this extraction displaces the existing one when both claim the same surface form:
    /// classified beats unclassified whatever the confidence, otherwise higher confidence wins.
    /// Strict, a tie keeps the existing one.
    /// </summary>
    public bool Outranks(ExtractedEntity existing) {
        if (IsClassified && !existing.IsClassified) return true;
        if (!IsClassified && existing.IsClassified) return false;

        return Confidence > existing.Confidence;
    }
}
