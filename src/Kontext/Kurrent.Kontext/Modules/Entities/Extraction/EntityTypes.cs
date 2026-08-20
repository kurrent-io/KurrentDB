// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// The canonical entity-type vocabulary, POLE+O (person, organization, location, event, object),
/// and the synonym fold-in that maps raw extractor labels onto it. The vocabulary anchors prompts
/// and keeps types shared across sources, but stays open: an unrecognized label passes through
/// lowercased instead of being coerced.
/// </summary>
public static class EntityTypes {
    public const string Person       = "person";
    public const string Organization = "organization";
    public const string Location     = "location";
    public const string Event        = "event";
    public const string Object       = "object";

    /// <summary>The non-type: the extractor found a span but could not classify it.</summary>
    public const string Unknown = "unknown";

    /// <summary>The types extraction prompts ask for.</summary>
    public static readonly IReadOnlyList<string> Canonical = [Person, Organization, Location, Event, Object];

    static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase) {
        ["individual"]  = Person,
        ["human"]       = Person,
        ["company"]     = Organization,
        ["org"]         = Organization,
        ["institution"] = Organization,
        ["place"]       = Location,
        ["city"]        = Location,
        ["country"]     = Location,
        ["address"]     = Location,
        ["incident"]    = Event,
        ["meeting"]     = Event,
        ["date"]        = Event,
        ["time"]        = Event,
        ["concept"]     = Object,
        ["product"]     = Object,
        ["thing"]       = Object,
        ["item"]        = Object,
        ["fact"]        = Object,
        ["preference"]  = Object,
        ["emotion"]     = Object,
    };

    /// <summary>
    /// Folds a raw label into the vocabulary: trim + lowercase, known synonyms land on their
    /// canonical type, everything else passes through. Blank means the extractor said nothing.
    /// </summary>
    public static string Normalize(string? entityType) {
        var trimmed = entityType?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return Unknown;

        var lowered = trimmed.ToLowerInvariant();

        return Synonyms.GetValueOrDefault(lowered, lowered);
    }
}
