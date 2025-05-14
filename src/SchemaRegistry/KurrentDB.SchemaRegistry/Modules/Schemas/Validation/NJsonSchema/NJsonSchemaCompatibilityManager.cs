// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault

using NJsonSchema;

namespace Kurrent.Surge.Schema.Validation;

[PublicAPI]
public class NJsonSchemaCompatibilityManager : SchemaCompatibilityManagerBase {
    protected override async ValueTask<SchemaCompatibilityResult> CheckCompatibilityCore(
        string uncheckedSchema, string referenceSchema,
        SchemaCompatibilityMode compatibility,
        CancellationToken cancellationToken = default
    ) {
        var results = await Task
            .WhenAll(
                JsonSchema.FromJsonAsync(uncheckedSchema, cancellationToken),
                JsonSchema.FromJsonAsync(referenceSchema, cancellationToken)
            )
            .ConfigureAwait(false);

        return CheckCompatibility(results[0], results[1], compatibility);
    }

    internal static SchemaCompatibilityResult CheckCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema, SchemaCompatibilityMode compatibility) =>
        compatibility switch {
            SchemaCompatibilityMode.None        => SchemaCompatibilityResult.Compatible(),
            SchemaCompatibilityMode.Backward    => CheckBackwardCompatibility(referenceSchema, uncheckedSchema),
            SchemaCompatibilityMode.Forward     => CheckForwardCompatibility(referenceSchema, uncheckedSchema),
            SchemaCompatibilityMode.Full        => CheckFullCompatibility(referenceSchema, uncheckedSchema),
            SchemaCompatibilityMode.Unspecified => throw new ArgumentException("Unspecified compatibility mode", nameof(compatibility)),
            _                                   => throw new ArgumentException($"Invalid compatibility mode", nameof(compatibility))

        };

    static SchemaCompatibilityResult CheckFullCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
        var backwardResult = CheckBackwardCompatibility(referenceSchema, uncheckedSchema);
        return backwardResult.IsCompatible
            ? CheckForwardCompatibility(referenceSchema, uncheckedSchema)
            : backwardResult;
    }

    static SchemaCompatibilityResult CheckBackwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
        var errors = new List<SchemaCompatibilityError>();

        CheckBackwardCompatibilityProperties(referenceSchema, uncheckedSchema, errors, "#");

        return errors.Count > 0
            ? SchemaCompatibilityResult.Incompatible(errors)
            : SchemaCompatibilityResult.Compatible();
    }

    static void CheckBackwardCompatibilityProperties(JsonSchema referenceSchema, JsonSchema otherSchema, List<SchemaCompatibilityError> errors, string path) {
        // Resolve references to their actual schema objects
        var resolvedRegisteredSchema = ResolveReference(referenceSchema);
        var resolvedOtherSchema      = ResolveReference(otherSchema);

        // Backward compatibility: New schema can process old data
        foreach (var (propertyName, registeredProperty) in resolvedRegisteredSchema.Properties) {
            var propertyPath = $"{path}/{propertyName}";

            // Resolve any references in the property
            var resolvedRegisteredProperty = ResolveReference(registeredProperty);

            // Check if property exists in the new schema
            if (!resolvedOtherSchema.Properties.TryGetValue(propertyName, out var otherProperty)) {
                // If the property is required in the registered schema but missing in the new one,
                // that's a backward compatibility issue
                if (resolvedRegisteredSchema.RequiredProperties.Contains(propertyName))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.MissingRequiredProperty,
                            PropertyPath = propertyPath,
                            Details      = "Required property in original schema is missing in new schema"
                        }
                    );

                continue;
            }

            // Resolve any references in the other property
            var resolvedOtherProperty = ResolveReference(otherProperty);

            // Check type compatibility
            if (!AreTypesCompatible(resolvedRegisteredProperty, resolvedOtherProperty))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.IncompatibleTypeChange,
                        PropertyPath = propertyPath,
                        Details      = "Property has incompatible type change",
                        OriginalType = resolvedRegisteredProperty.Type,
                        NewType      = resolvedOtherProperty.Type
                    }
                );

            // Check if a property changed from optional to required
            if (!resolvedRegisteredSchema.RequiredProperties.Contains(propertyName) && resolvedOtherSchema.RequiredProperties.Contains(propertyName))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.OptionalToRequired,
                        PropertyPath = propertyPath,
                        Details      = "Property changed from optional to required, breaking backward compatibility"
                    }
                );

            // Recursively check nested objects
            if (resolvedRegisteredProperty.Type == JsonObjectType.Object && resolvedOtherProperty.Type == JsonObjectType.Object)
                CheckBackwardCompatibilityProperties(resolvedRegisteredProperty, resolvedOtherProperty, errors, propertyPath);

            // Check array items
            if (resolvedRegisteredProperty.Type == JsonObjectType.Array
                && resolvedOtherProperty.Type   == JsonObjectType.Array
                && resolvedRegisteredProperty.Item is not null
                && resolvedOtherProperty.Item is not null) {
                var resolvedRegisteredItem = ResolveReference(resolvedRegisteredProperty.Item);
                var resolvedOtherItem      = ResolveReference(resolvedOtherProperty.Item);

                if (!AreTypesCompatible(resolvedRegisteredItem, resolvedOtherItem))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
                            PropertyPath = propertyPath,
                            Details      = "Array items have incompatible type change",
                            OriginalType = resolvedRegisteredItem.Type,
                            NewType      = resolvedOtherItem.Type
                        }
                    );

                // If array items are objects, check them recursively
                if (resolvedRegisteredItem.Type == JsonObjectType.Object && resolvedOtherItem.Type == JsonObjectType.Object)
                    CheckBackwardCompatibilityProperties(resolvedRegisteredItem, resolvedOtherItem, errors, $"{propertyPath}/items");
            }
        }
    }

    static SchemaCompatibilityResult CheckForwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
        var errors = new List<SchemaCompatibilityError>();

        CheckForwardCompatibilityProperties(referenceSchema, uncheckedSchema, errors, "#");

        return errors.Count > 0
            ? SchemaCompatibilityResult.Incompatible(errors)
            : SchemaCompatibilityResult.Compatible();
    }

    static void CheckForwardCompatibilityProperties(JsonSchema referenceSchema, JsonSchema otherSchema, List<SchemaCompatibilityError> errors, string path) {
        // Resolve references to their actual schema objects
        var resolvedRegisteredSchema = ResolveReference(referenceSchema);
        var resolvedOtherSchema      = ResolveReference(otherSchema);

        // Forward compatibility: Old schema can process new data
        foreach (var (propertyName, otherProperty) in resolvedOtherSchema.Properties) {
            var propertyPath = $"{path}/{propertyName}";

            // Resolve any references in the property
            var resolvedOtherProperty = ResolveReference(otherProperty);

            // Check if property exists in registered schema
            if (!resolvedRegisteredSchema.Properties.TryGetValue(propertyName, out var registeredProperty)) {
                // If the property is required in the new schema but missing in the registered one,
                // that's a forward compatibility issue
                if (resolvedOtherSchema.RequiredProperties.Contains(propertyName))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.NewRequiredProperty,
                            PropertyPath = propertyPath,
                            Details      = "Required property in new schema is missing in original schema"
                        }
                    );

                continue;
            }

            // Resolve any references in the registered property
            var resolvedRegisteredProperty = ResolveReference(registeredProperty);

            // Check type compatibility
            if (!AreTypesCompatible(resolvedOtherProperty, resolvedRegisteredProperty))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.IncompatibleTypeChange,
                        PropertyPath = propertyPath,
                        Details      = "Property has incompatible type change",
                        OriginalType = resolvedOtherProperty.Type,
                        NewType      = resolvedRegisteredProperty.Type
                    }
                );

            // Recursively check nested objects
            if (resolvedOtherProperty.Type == JsonObjectType.Object && resolvedRegisteredProperty.Type == JsonObjectType.Object)
                CheckForwardCompatibilityProperties(resolvedRegisteredProperty, resolvedOtherProperty, errors, propertyPath);

            // Check array items
            if (resolvedOtherProperty.Type         == JsonObjectType.Array
                && resolvedRegisteredProperty.Type == JsonObjectType.Array
                && resolvedOtherProperty.Item is not null
                && resolvedRegisteredProperty.Item is not null) {
                var resolvedOtherItem      = ResolveReference(resolvedOtherProperty.Item);
                var resolvedRegisteredItem = ResolveReference(resolvedRegisteredProperty.Item);

                if (!AreTypesCompatible(resolvedOtherItem, resolvedRegisteredItem))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
                            PropertyPath = propertyPath,
                            Details      = "Array items have incompatible type change",
                            OriginalType = resolvedOtherItem.Type,
                            NewType      = resolvedRegisteredItem.Type
                        }
                    );

                // If array items are objects, check them recursively
                if (resolvedOtherItem.Type == JsonObjectType.Object && resolvedRegisteredItem.Type == JsonObjectType.Object) {
                    CheckForwardCompatibilityProperties(resolvedRegisteredItem, resolvedOtherItem, errors, $"{propertyPath}/items");
                }
            }
        }

        // Check for properties in registered schema that are missing in other schema
        foreach (var (propertyName, _) in resolvedRegisteredSchema.Properties) {
            var propertyPath = $"{path}/{propertyName}";

            if (!resolvedOtherSchema.Properties.ContainsKey(propertyName)) {
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.RemovedProperty,
                        PropertyPath = propertyPath,
                        Details      = "Property in original schema is missing in new schema"
                    }
                );
            }
        }
    }

    static bool AreTypesCompatible(JsonSchema schema1, JsonSchema schema2) {
        // Resolve any references in the schemas
        var resolvedSchema1 = ResolveReference(schema1);
        var resolvedSchema2 = ResolveReference(schema2);

        // Basic type compatibility check
        if (resolvedSchema1.Type != resolvedSchema2.Type)
            return false;

        // For arrays, check item compatibility
        // Both should be null or both non-null
        if (resolvedSchema1.Type == JsonObjectType.Array)
            return resolvedSchema1.Item is null || resolvedSchema2.Item is null
                ? resolvedSchema1.Item == resolvedSchema2.Item
                : AreTypesCompatible(resolvedSchema1.Item, resolvedSchema2.Item);

        // For objects, we'll do a simplified check here
        // More detailed checks are done recursively in the compatibility methods
        if (resolvedSchema1.Type == JsonObjectType.Object)
            return true;

        // For other types, basic type equality check is enough
        return true;
    }

    /// <summary>
    /// Resolves a JSON schema reference to its actual schema object
    /// </summary>
    /// <param name="schema">The schema that may contain a reference</param>
    /// <returns>The resolved schema, or the original if it wasn't a reference</returns>
    static JsonSchema ResolveReference(JsonSchema schema) {
        // If the schema has a reference, resolve it recursively
        if (schema is { HasReference: true, Reference: not null })
            return ResolveReference(schema.Reference);

        // If it's a reference to an unresolved schema, we can't do much
        // This shouldn't happen with NJsonSchema, but adding as a safeguard
        if (schema is { HasReference: true, Reference: null })
            return schema;

        // Return the schema itself if it's not a reference
        return schema;
    }
}