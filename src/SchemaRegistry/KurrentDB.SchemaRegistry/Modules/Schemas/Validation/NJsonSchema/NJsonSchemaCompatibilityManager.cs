// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ConvertIfStatementToSwitchStatement
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using NJsonSchema;

namespace Kurrent.Surge.Schema.Validation;

public delegate SchemaCompatibilityResult CheckSchemaCompatibility(JsonSchema reference, JsonSchema uncheckedSchema);

[PublicAPI]
public class NJsonSchemaCompatibilityManager : SchemaCompatibilityManagerBase {
	static readonly SchemaCompatibilityChecker CompatibilityChecker = new();

	protected override async ValueTask<SchemaCompatibilityResult> CheckCompatibilityCore(
		string uncheckedSchema,
		string[] referenceSchemas,
		SchemaCompatibilityMode compatibility,
		CancellationToken cancellationToken = default)
	{
		var uncheckedJsonSchema = await JsonSchema
			.FromJsonAsync(uncheckedSchema, cancellationToken)
			.ConfigureAwait(false);

		var referenceJsonSchemas = await Task
			.WhenAll(referenceSchemas.AsParallel().Select(s => JsonSchema.FromJsonAsync(s, cancellationToken)))
			.ConfigureAwait(false);

		return CheckCompatibility(referenceJsonSchemas, uncheckedJsonSchema, compatibility);
	}

	internal static SchemaCompatibilityResult CheckCompatibility(
		JsonSchema referenceSchemas, JsonSchema uncheckedSchema, SchemaCompatibilityMode compatibility
	) => CheckCompatibility([referenceSchemas], uncheckedSchema, compatibility);

	internal static SchemaCompatibilityResult CheckCompatibility(
		IList<JsonSchema> referenceSchemas, JsonSchema uncheckedSchema, SchemaCompatibilityMode compatibility
	) => compatibility switch {
		SchemaCompatibilityMode.None        => SchemaCompatibilityResult.Compatible(),
		SchemaCompatibilityMode.Backward    => CheckBackwardCompatibility(referenceSchemas.First(), uncheckedSchema),
		SchemaCompatibilityMode.Forward     => CheckForwardCompatibility(referenceSchemas.First(), uncheckedSchema),
		SchemaCompatibilityMode.Full        => CheckFullCompatibility(referenceSchemas.First(), uncheckedSchema),
		SchemaCompatibilityMode.BackwardAll => CheckBackwardAllCompatibility(referenceSchemas, uncheckedSchema),
		SchemaCompatibilityMode.ForwardAll  => CheckForwardAllCompatibility(referenceSchemas, uncheckedSchema),
		SchemaCompatibilityMode.FullAll     => CheckFullAllCompatibility(referenceSchemas, uncheckedSchema),
		SchemaCompatibilityMode.Unspecified => throw new ArgumentException("Unspecified compatibility mode", nameof(compatibility)),
		_                                   => throw new ArgumentException("Invalid compatibility mode", nameof(compatibility))
	};

	static SchemaCompatibilityResult CheckBackwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) =>
		CompatibilityChecker.CheckBackwardCompatibility(referenceSchema, uncheckedSchema);

	static SchemaCompatibilityResult CheckForwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) =>
		CompatibilityChecker.CheckForwardCompatibility(referenceSchema, uncheckedSchema);

	static SchemaCompatibilityResult CheckFullCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
		var backwardResult = CheckBackwardCompatibility(referenceSchema, uncheckedSchema);
		return backwardResult.IsCompatible
			? CheckForwardCompatibility(referenceSchema, uncheckedSchema)
			: backwardResult;
	}

	static SchemaCompatibilityResult CheckFullAllCompatibility(IList<JsonSchema> referenceSchemas, JsonSchema uncheckedSchema) =>
		CheckAllCompatibility(referenceSchemas, uncheckedSchema, CheckFullCompatibility);

	static SchemaCompatibilityResult CheckBackwardAllCompatibility(IList<JsonSchema> referenceSchemas, JsonSchema uncheckedSchema) =>
		CheckAllCompatibility(referenceSchemas, uncheckedSchema, CheckBackwardCompatibility);

	static SchemaCompatibilityResult CheckForwardAllCompatibility(IList<JsonSchema> referenceSchemas, JsonSchema uncheckedSchema) =>
		CheckAllCompatibility(referenceSchemas, uncheckedSchema, CheckForwardCompatibility);

	static SchemaCompatibilityResult CheckAllCompatibility(IList<JsonSchema> referenceSchemas, JsonSchema uncheckedSchema,
		CheckSchemaCompatibility checkCompatibility) {
		var errors = referenceSchemas
			.AsParallel()
			.Select(referenceSchema => checkCompatibility(referenceSchema, uncheckedSchema))
			.Where(result => !result.IsCompatible)
			.SelectMany(result => result.Errors)
			.ToList();

		return errors.Count > 0
			? SchemaCompatibilityResult.Incompatible(errors)
			: SchemaCompatibilityResult.Compatible();
	}
}

internal class SchemaCompatibilityChecker {
	readonly HashSet<(JsonSchema, JsonSchema)> VisitedSchemas = [];

	public SchemaCompatibilityResult CheckBackwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
		VisitedSchemas.Clear();
		var errors = new List<SchemaCompatibilityError>();
		CheckBackwardCompatibilityProperties(referenceSchema, uncheckedSchema, errors);

		return errors.Count > 0
			? SchemaCompatibilityResult.Incompatible(errors)
			: SchemaCompatibilityResult.Compatible();
	}

	public SchemaCompatibilityResult CheckForwardCompatibility(JsonSchema referenceSchema, JsonSchema uncheckedSchema) {
		VisitedSchemas.Clear();
		var errors = new List<SchemaCompatibilityError>();
		CheckForwardCompatibilityProperties(referenceSchema, uncheckedSchema, errors);

		return errors.Count > 0
			? SchemaCompatibilityResult.Incompatible(errors)
			: SchemaCompatibilityResult.Compatible();
	}

	void CheckBackwardCompatibilityProperties(
		JsonSchema referenceSchema,
		JsonSchema uncheckedSchema,
		List<SchemaCompatibilityError> errors,
		string path = "#"
	) {
		referenceSchema = ResolveReference(referenceSchema);
		uncheckedSchema = ResolveReference(uncheckedSchema);

		var schemaKey = (referenceSchema, uncheckedSchema);
		if (!VisitedSchemas.Add(schemaKey))
			return;

		foreach (var (name, value) in referenceSchema.Properties) {
			var propertyPath = $"{path}/{name}";
			var resolvedValue = ResolveReference(value);

			// Handle missing properties
			if (!uncheckedSchema.Properties.TryGetValue(name, out var uncheckedSchemaProperty)) {
				if (referenceSchema.RequiredProperties.Contains(name))
					AddMissingRequiredPropertyError(errors, propertyPath);

				continue;
			}

			// Compare property types and requirements
			var resolvedUncheckedValue = ResolveReference(uncheckedSchemaProperty);
			CheckPropertyTypeCompatibility(resolvedValue, resolvedUncheckedValue, errors, propertyPath);
			CheckOptionalToRequiredChange(referenceSchema, uncheckedSchema, name, errors, propertyPath);

			// Handle nested structures (objects and arrays)
			CheckNestedStructures(resolvedValue, resolvedUncheckedValue, errors, propertyPath);
		}

		// Check for new required properties that old data won't have
		CheckNewRequiredProperties(referenceSchema, uncheckedSchema, errors, path);
	}

	void CheckForwardCompatibilityProperties(
		JsonSchema referenceSchema,
		JsonSchema uncheckedSchema,
		List<SchemaCompatibilityError> errors,
		string path = "#"
	) {
		referenceSchema = ResolveReference(referenceSchema);
		uncheckedSchema = ResolveReference(uncheckedSchema);

		var schemaKey = (referenceSchema, uncheckedSchema);
		if (!VisitedSchemas.Add(schemaKey))
			return;

		foreach (var (name, value) in uncheckedSchema.Properties) {
			var propertyPath = $"{path}/{name}";
			var resolvedValue = ResolveReference(value);

			// Handle missing properties
			if (!referenceSchema.Properties.TryGetValue(name, out var referenceSchemaProperty)) {
				if (uncheckedSchema.RequiredProperties.Contains(name))
					AddNewRequiredPropertyError(errors, propertyPath);

				continue;
			}

			// Compare property types
			var resolvedReferenceProperty = ResolveReference(referenceSchemaProperty);
			CheckPropertyTypeCompatibility(resolvedValue, resolvedReferenceProperty, errors, propertyPath);

			// Handle nested structures (objects and arrays)
			CheckNestedStructuresForward(resolvedReferenceProperty, resolvedValue, errors, propertyPath);
		}

		// Check for required properties in reference schema missing in unchecked schema
		CheckMissingRequiredProperties(referenceSchema, uncheckedSchema, errors, path);
	}

	static void AddMissingRequiredPropertyError(List<SchemaCompatibilityError> errors, string propertyPath) =>
		errors.Add(new SchemaCompatibilityError {
			Kind = SchemaCompatibilityErrorKind.MissingRequiredProperty,
			PropertyPath = propertyPath,
			Details = "Required property in original schema is missing in new schema"
		});

	static void AddNewRequiredPropertyError(List<SchemaCompatibilityError> errors, string propertyPath) =>
		errors.Add(new SchemaCompatibilityError {
			Kind = SchemaCompatibilityErrorKind.NewRequiredProperty,
			PropertyPath = propertyPath,
			Details = "Required property in new schema is missing in original schema"
		});

	static void CheckPropertyTypeCompatibility(JsonSchema schema1, JsonSchema schema2, List<SchemaCompatibilityError> errors, string propertyPath) {
		if (!AreTypesCompatible(schema1, schema2))
			errors.Add(new SchemaCompatibilityError {
				Kind = SchemaCompatibilityErrorKind.IncompatibleTypeChange,
				PropertyPath = propertyPath,
				Details = "Property has incompatible type change",
				OriginalType = schema1.Type,
				NewType = schema2.Type
			});
	}

	static void CheckOptionalToRequiredChange(JsonSchema referenceSchema, JsonSchema uncheckedSchema, string propertyName,
		List<SchemaCompatibilityError> errors,
		string propertyPath) {
		if (!referenceSchema.RequiredProperties.Contains(propertyName) &&
		    uncheckedSchema.RequiredProperties.Contains(propertyName)) {
			errors.Add(new SchemaCompatibilityError {
				Kind = SchemaCompatibilityErrorKind.OptionalToRequired,
				PropertyPath = propertyPath,
				Details = "Property changed from optional to required, breaking backward compatibility"
			});
		}
	}

	void CheckNestedStructures(JsonSchema referenceValue, JsonSchema uncheckedValue, List<SchemaCompatibilityError> errors, string propertyPath) {
		// Handle nested objects
		if (referenceValue.Type is JsonObjectType.Object && uncheckedValue.Type is JsonObjectType.Object) {
			CheckBackwardCompatibilityProperties(referenceValue, uncheckedValue, errors, propertyPath);
		}

		// Handle arrays with items
		if (referenceValue.Type is JsonObjectType.Array &&
		    uncheckedValue.Type is JsonObjectType.Array &&
		    referenceValue.Item is not null &&
		    uncheckedValue.Item is not null) {
			CheckArrayItemCompatibility(referenceValue.Item, uncheckedValue.Item, errors, propertyPath);
		}
	}

	void CheckNestedStructuresForward(JsonSchema referenceValue, JsonSchema uncheckedValue, List<SchemaCompatibilityError> errors, string propertyPath) {
		// Handle nested objects
		if (uncheckedValue.Type is JsonObjectType.Object && referenceValue.Type is JsonObjectType.Object) {
			CheckForwardCompatibilityProperties(referenceValue, uncheckedValue, errors, propertyPath);
		}

		// Handle arrays with items
		if (uncheckedValue.Type is JsonObjectType.Array &&
		    referenceValue.Type is JsonObjectType.Array &&
		    uncheckedValue.Item is not null &&
		    referenceValue.Item is not null) {
			CheckArrayItemCompatibilityForward(referenceValue.Item, uncheckedValue.Item, errors, propertyPath);
		}
	}

	void CheckArrayItemCompatibility(JsonSchema referenceItem, JsonSchema uncheckedItem, List<SchemaCompatibilityError> errors,
		string propertyPath) {
		var resolvedReferenceItem = ResolveReference(referenceItem);
		var resolvedUncheckedItem = ResolveReference(uncheckedItem);

		// Check array item type compatibility
		if (!AreTypesCompatible(resolvedReferenceItem, resolvedUncheckedItem)) {
			errors.Add(new SchemaCompatibilityError {
				Kind = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
				PropertyPath = propertyPath,
				Details = "Array items have incompatible type change",
				OriginalType = resolvedReferenceItem.Type,
				NewType = resolvedUncheckedItem.Type
			});
		}

		// If array items are objects, check them recursively
		if (resolvedReferenceItem.Type is JsonObjectType.Object &&
		    resolvedUncheckedItem.Type is JsonObjectType.Object) {
			CheckBackwardCompatibilityProperties(
				resolvedReferenceItem,
				resolvedUncheckedItem,
				errors,
				$"{propertyPath}/items");
		}
	}

	void CheckArrayItemCompatibilityForward(JsonSchema referenceItem, JsonSchema uncheckedItem, List<SchemaCompatibilityError> errors, string propertyPath) {
		var resolvedReferenceItem = ResolveReference(referenceItem);
		var resolvedUncheckedItem = ResolveReference(uncheckedItem);

		// Check array item type compatibility
		if (!AreTypesCompatible(resolvedUncheckedItem, resolvedReferenceItem)) {
			errors.Add(new SchemaCompatibilityError {
				Kind = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
				PropertyPath = propertyPath,
				Details = "Array items have incompatible type change",
				OriginalType = resolvedUncheckedItem.Type,
				NewType = resolvedReferenceItem.Type
			});
		}

		// If array items are objects, check them recursively
		if (resolvedUncheckedItem.Type is JsonObjectType.Object &&
		    resolvedReferenceItem.Type is JsonObjectType.Object) {
			CheckForwardCompatibilityProperties(
				resolvedReferenceItem,
				resolvedUncheckedItem,
				errors,
				$"{propertyPath}/items");
		}
	}

	static void CheckNewRequiredProperties(JsonSchema referenceSchema, JsonSchema uncheckedSchema, List<SchemaCompatibilityError> errors, string path) {
		foreach (var (propertyName, _) in uncheckedSchema.Properties) {
			if (!referenceSchema.Properties.ContainsKey(propertyName) &&
			    uncheckedSchema.RequiredProperties.Contains(propertyName)) {
				errors.Add(new SchemaCompatibilityError {
					Kind = SchemaCompatibilityErrorKind.NewRequiredProperty,
					PropertyPath = $"{path}/{propertyName}",
					Details = "New required property breaks backward compatibility - old data won't have this field"
				});
			}
		}
	}

	static void CheckMissingRequiredProperties(JsonSchema referenceSchema, JsonSchema uncheckedSchema, List<SchemaCompatibilityError> errors, string path) {
		foreach (var (propertyName, _) in referenceSchema.Properties) {
			if (!uncheckedSchema.Properties.ContainsKey(propertyName) &&
			    referenceSchema.RequiredProperties.Contains(propertyName)) {
				errors.Add(new SchemaCompatibilityError {
					Kind = SchemaCompatibilityErrorKind.RemovedProperty,
					PropertyPath = $"{path}/{propertyName}",
					Details = "Required property in original schema is missing in new schema"
				});
			}
		}
	}

	static bool AreTypesCompatible(JsonSchema sourceSchema, JsonSchema targetSchema) {
		// Resolve any references in the schemas
		sourceSchema = ResolveReference(sourceSchema);
		targetSchema = ResolveReference(targetSchema);

		// Basic type compatibility check
		if (sourceSchema.Type != targetSchema.Type)
			return false;

		return sourceSchema.Type switch {
			// For arrays, check item compatibility
			// Both should be null or both non-null
			JsonObjectType.Array => sourceSchema.Item is null || targetSchema.Item is null
				? sourceSchema.Item == targetSchema.Item
				: AreTypesCompatible(sourceSchema.Item, targetSchema.Item),

			// For objects, we'll do a simplified check here
			// More detailed checks are done recursively in the compatibility methods
			JsonObjectType.Object => true,

			// For other types, basic type equality check is enough
			_ => true
		};
	}

	/// <summary>
	/// Resolves a JSON schema reference to its actual schema object
	/// </summary>
	/// <param name="schema">The schema that may contain a reference</param>
	/// <returns>The resolved schema, or the original if it wasn't a reference</returns>
	static JsonSchema ResolveReference(JsonSchema schema) {
		return schema switch {
			// If the schema has a reference, resolve it recursively
			{ HasReference: true, Reference: not null } => ResolveReference(schema.Reference),

			// If it's a reference to an unresolved schema, we can't do much
			// This shouldn't happen with NJsonSchema, but adding as a safeguard
			{ HasReference: true, Reference: null } => schema,

			// Return the schema itself if it's not a reference
			_ => schema
		};
	}
}
