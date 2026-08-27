// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;

namespace Kurrent.Kontext.Entities.Extraction;

/// <summary>
/// The canonical entity-type vocabulary (POLE+O: person, organization, location, event, object)
/// and the synonym fold that maps raw extractor labels onto it. The vocabulary is open:
/// unrecognized labels pass through lowercased rather than being coerced.
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

    /// <summary>
    /// Everyday labels the abstract five under-recall: GLiNER fires on "activity" and "animal"
    /// where "event" and "object" stay silent. They pass through as open-vocabulary types.
    /// </summary>
    public static readonly IReadOnlyList<string> Everyday = ["activity", "animal", "food", "creative work", "health condition"];

    /// <summary>The label vocabulary zero-shot extraction runs with.</summary>
    public static readonly IReadOnlyList<string> ExtractionLabels = [..Canonical, ..Everyday];

    static readonly FrozenDictionary<string, string> Synonyms = new Dictionary<string, string> {
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
    }.ToFrozenDictionary();

    /// <summary>
    /// Folds a raw label into the vocabulary: trim + lowercase, synonyms land on their canonical
    /// type, everything else passes through. Blank folds to <see cref="Unknown"/>.
    /// </summary>
    public static string Normalize(string? entityType) =>
        entityType?.Trim().ToLowerInvariant() switch {
            null or "" => Unknown,
            var lowered => Synonyms.GetValueOrDefault(lowered, lowered)
        };
}
