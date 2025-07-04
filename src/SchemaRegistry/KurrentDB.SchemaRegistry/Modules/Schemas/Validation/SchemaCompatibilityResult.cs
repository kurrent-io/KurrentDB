// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NJsonSchema;

namespace Kurrent.Surge.Schema.Validation;

public record SchemaCompatibilityResult {
    public bool                                    IsCompatible { get; private init; }
    public IReadOnlyList<SchemaCompatibilityError> Errors       { get; private init; } = [];

    public static SchemaCompatibilityResult Compatible() =>
        new() { IsCompatible = true };

    public static SchemaCompatibilityResult Incompatible(IEnumerable<SchemaCompatibilityError> errors) =>
        new() {
            IsCompatible = false,
            Errors       = errors.ToList()
        };

    public static SchemaCompatibilityResult Incompatible(SchemaCompatibilityError error) =>
        new() {
            IsCompatible = false,
            Errors       = [error]
        };
}

public record SchemaCompatibilityError {
    public SchemaCompatibilityErrorKind Kind         { get; init; }
    public string                       PropertyPath { get; init; } = string.Empty;
    public string                       Details      { get; init; } = string.Empty;
    public JsonObjectType?              OriginalType { get; init; }
    public JsonObjectType?              NewType      { get; init; }

    public override string ToString() =>
        $"{Kind} at '{PropertyPath}': {Details}";
}

public enum SchemaCompatibilityErrorKind {
    Unspecified,                  // Unspecified error, should not be used
    MissingRequiredProperty,      // Backward compatibility: Required property from old schema missing in new schema
    IncompatibleTypeChange,       // Backward compatibility: Property type changed incompatibly
    OptionalToRequired,           // Backward compatibility: Property changed from optional to required
    NewRequiredProperty,          // Forward compatibility: New required property added
    RemovedProperty,              // Forward compatibility: Property removed from schema
    ArrayTypeIncompatibility,     // Issues with array item types
}
